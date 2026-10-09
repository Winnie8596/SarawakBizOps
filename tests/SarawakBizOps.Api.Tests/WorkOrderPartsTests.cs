using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 7: parts used on a job (PRD §6.5 second half, SDD §10/§18). AC-03 (one WorkOrderPart + exactly one
/// Issue row + lower stock, atomically — including a simulated failure mid-way), AC-04 (not enough stock:
/// refused, nothing changes), the concurrent-issue race for the last units (the flagship test), returns,
/// and PendingParts/resume. Every stock-moving test ends by reconciling the whole database.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkOrderPartsTests
{
    private readonly ApiFactory _factory;

    public WorkOrderPartsTests(ApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    private static async Task<IReadOnlyList<InventoryTransactionDto>> JobLedgerAsync(HttpClient client, int workOrderId)
        => (await client.GetFromJsonAsync<CursorPage<InventoryTransactionDto>>($"/api/inventory-transactions?workOrderId={workOrderId}"))!.Items;

    /// <summary>A fresh technician with an InProgress job of their own, plus admin and manager clients.</summary>
    private async Task<(HttpClient Admin, HttpClient Manager, HttpClient Technician, TestUser User, WorkOrderDto Job)> JobInProgressAsync()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var job = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        return (admin, manager, technician, user, job);
    }

    // ---- AC-03: add a part ----

    [Fact]
    public async Task Adding_a_part_creates_one_line_and_exactly_one_issue_row_and_lowers_stock()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 5, unitCost: 145.00m);

        var response = await PostAddPartAsync(technician, job.Id, part.Id, 2);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
        var line = Assert.Single(updated.Parts);
        Assert.Equal(part.Id, line.PartId);
        Assert.Equal(part.PartNumber, line.PartNumber);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(145.00m, line.UnitPrice);
        Assert.Equal(290.00m, line.LineTotal);
        Assert.Equal(290.00m, updated.PartsTotal);
        Assert.NotEqual(job.RowVersion, updated.RowVersion); // the job's parts changed: stale screens get a 409

        Assert.Equal(3m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
        var issue = Assert.Single(await JobLedgerAsync(admin, job.Id));
        Assert.Equal("Issue", issue.TransactionType);
        Assert.Equal(-2m, issue.Quantity);
        Assert.Equal(part.Id, issue.PartId);
        Assert.Equal(job.Id, issue.WorkOrderId);
        Assert.Equal("Test Technician", issue.CreatedByName);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task The_price_is_snapshotted_from_the_catalogue_and_a_client_price_is_ignored()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 10, unitCost: 20.00m);

        // A crafted body that tries to set its own price: not part of the contract.
        var crafted = await technician.PostAsJsonAsync($"/api/work-orders/{job.Id}/parts",
            new { partId = part.Id, quantity = 1m, unitPrice = 0.01m, rowVersion = job.RowVersion });
        Assert.Equal(HttpStatusCode.OK, crafted.StatusCode);

        // A later price change does not rewrite the first line; a new line takes the new price.
        var current = await GetPartAsync(admin, part.Id);
        var reprice = await admin.PutAsJsonAsync($"/api/parts/{part.Id}", new UpdatePartRequest
        {
            PartNumber = current.PartNumber, Name = current.Name, Unit = current.Unit, UnitCost = 35.00m,
            MinimumStockLevel = current.MinimumStockLevel, IsActive = true, RowVersion = current.RowVersion
        });
        reprice.EnsureSuccessStatusCode();
        var updated = await AddPartAsync(technician, job.Id, part.Id, 1);

        Assert.Equal(new[] { 20.00m, 35.00m }, updated.Parts.Select(p => p.UnitPrice));
        Assert.Equal(55.00m, updated.PartsTotal);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Theory]
    [InlineData(FailurePoint.BeforeRowsAreWritten)]
    [InlineData(FailurePoint.AfterRowsAreWritten)]
    public async Task A_failure_part_way_through_adding_a_part_leaves_nothing_behind(FailurePoint failurePoint)
    {
        var (admin, _, _, user, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 4);

        // A second host on the same database whose DbContext throws while saving the line and Issue row —
        // after the stock UPDATE and the job claim have already run inside the transaction.
        var connectionString = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options
                .UseSqlServer(connectionString)
                .AddInterceptors(new FailOnIssue(failurePoint)));
        }));
        var technician = await SignInAsync(failing, user);

        var response = await PostAddPartAsync(technician, job.Id, part.Id, 1);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var reloaded = (await GetWorkOrderAsync(admin, job.Id))!;
        Assert.Empty(reloaded.Parts);
        Assert.Equal(job.RowVersion, reloaded.RowVersion); // even the job claim was rolled back
        Assert.Equal(4m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
        Assert.Empty(await JobLedgerAsync(admin, job.Id));
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    public enum FailurePoint { BeforeRowsAreWritten, AfterRowsAreWritten }

    private sealed class FailOnIssue : SaveChangesInterceptor
    {
        private readonly FailurePoint _point;
        private bool _issuePending;

        public FailOnIssue(FailurePoint point) => _point = point;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            _issuePending = eventData.Context!.ChangeTracker.Entries<InventoryTransaction>()
                .Any(e => e.State == EntityState.Added && e.Entity.TransactionType == InventoryTransactionType.Issue);
            if (_issuePending && _point == FailurePoint.BeforeRowsAreWritten)
            {
                throw new InvalidOperationException("Simulated failure before the line and Issue row are written.");
            }
            return base.SavingChangesAsync(eventData, result, ct);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        {
            if (_issuePending && _point == FailurePoint.AfterRowsAreWritten)
            {
                throw new InvalidOperationException("Simulated failure after the rows are written, before commit.");
            }
            return base.SavedChangesAsync(eventData, result, ct);
        }
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, TestUser user)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = user.Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---- AC-04: not enough stock ----

    [Fact]
    public async Task Adding_more_than_is_in_stock_is_refused_and_changes_nothing()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 2);

        var response = await PostAddPartAsync(technician, job.Id, part.Id, 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Not enough stock: only 2 pcs", await DetailAsync(response));
        var reloaded = (await GetWorkOrderAsync(technician, job.Id))!;
        Assert.Empty(reloaded.Parts);
        Assert.Equal(job.RowVersion, reloaded.RowVersion);
        Assert.Equal(2m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
        Assert.Empty(await JobLedgerAsync(admin, job.Id));
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    // ---- The concurrent-issue test (PRD §6.5, SDD §18): the flagship ----

    public static TheoryData<int, decimal, int, decimal> Races() => new()
    {
        // round, stock on hand, technicians racing, quantity each
        { 1, 3m, 8, 1m },
        { 2, 1m, 6, 1m },
        { 3, 2.5m, 8, 0.5m },
        { 4, 3m, 8, 1m },
        { 5, 1m, 6, 1m }
    };

    [Theory]
    [MemberData(nameof(Races))]
    public async Task Technicians_racing_for_the_last_units_never_take_more_than_there_is(
        int round, decimal onHand, int racers, decimal each)
    {
        _ = round; // repeats the race with fresh data on every run of the suite
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var part = await CreateStockedPartAsync(admin, onHand);

        // Each technician has their own job, so the only thing they contend for is the part's stock row.
        // Setup is sequential; only the part requests below race.
        var contenders = new List<(HttpClient Client, WorkOrderDto Job)>();
        for (var i = 0; i < racers; i++)
        {
            var (client, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
            contenders.Add((client, await CreateInProgressWorkOrderAsync(admin, manager, client, user.Id)));
        }

        var responses = await Task.WhenAll(contenders.Select(c =>
            c.Client.PostAsJsonAsync($"/api/work-orders/{c.Job.Id}/parts",
                new AddWorkOrderPartRequest { PartId = part.Id, Quantity = each, RowVersion = c.Job.RowVersion })));

        var winners = (int)(onHand / each);
        Assert.Equal(winners, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(racers - winners, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);

        var issues = (await admin.GetFromJsonAsync<CursorPage<InventoryTransactionDto>>(
            $"/api/inventory-transactions?partId={part.Id}&type=Issue"))!.Items;
        Assert.Equal(winners, issues.Count);
        // Every job that won has exactly one line; every job that lost has none.
        foreach (var (contender, response) in contenders.Zip(responses))
        {
            var job = (await GetWorkOrderAsync(contender.Client, contender.Job.Id))!;
            Assert.Equal(response.IsSuccessStatusCode ? 1 : 0, job.Parts.Count);
        }
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task A_double_tap_on_the_same_job_view_adds_the_part_once()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 10);

        // Both taps carry the RowVersion the screen was showing: the job claim lets exactly one through.
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            technician.PostAsJsonAsync($"/api/work-orders/{job.Id}/parts",
                new AddWorkOrderPartRequest { PartId = part.Id, Quantity = 1, RowVersion = job.RowVersion })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await GetWorkOrderAsync(technician, job.Id))!.Parts);
        Assert.Equal(9m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    // ---- Return ----

    [Fact]
    public async Task Returning_a_line_puts_its_stock_back_with_one_return_row()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 5);
        var withPart = await AddPartAsync(technician, job.Id, part.Id, 2);
        var lineId = withPart.Parts.Single().Id;

        var response = await DeletePartLineAsync(technician, job.Id, lineId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
        Assert.Empty(updated.Parts);
        Assert.Equal(0m, updated.PartsTotal);
        Assert.Equal(5m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);

        var ledger = await JobLedgerAsync(admin, job.Id);
        Assert.Equal(new[] { ("Return", 2m), ("Issue", -2m) }, ledger.Select(t => (t.TransactionType, t.Quantity)));
        Assert.All(ledger, t => Assert.Equal(job.Id, t.WorkOrderId));

        // The line is gone: returning it again is a 404 and adds no stock.
        var again = await DeletePartLineAsync(technician, job.Id, lineId);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(5m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task A_line_can_only_be_returned_through_its_own_job()
    {
        var (admin, manager, technician, user, job) = await JobInProgressAsync();
        var otherJob = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        var part = await CreateStockedPartAsync(admin, onHand: 5);
        var lineId = (await AddPartAsync(technician, job.Id, part.Id, 1)).Parts.Single().Id;

        var response = await DeletePartLineAsync(technician, otherJob.Id, lineId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single((await GetWorkOrderAsync(technician, job.Id))!.Parts);
        Assert.Equal(4m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
    }

    // ---- Who and when ----

    [Fact]
    public async Task Another_technician_cannot_see_or_change_parts_on_my_job()
    {
        var (admin, _, _, _, job) = await JobInProgressAsync();
        var intruder = await _factory.ClientWithRoleAsync(AppRoles.Technician);
        var part = await CreateStockedPartAsync(admin, onHand: 5);

        var add = await intruder.PostAsJsonAsync($"/api/work-orders/{job.Id}/parts",
            new AddWorkOrderPartRequest { PartId = part.Id, Quantity = 1, RowVersion = job.RowVersion });
        var pending = await intruder.PostAsJsonAsync($"/api/work-orders/{job.Id}/pending-parts",
            new PendingPartsRequest { Reason = "Not mine", PartId = part.Id, Quantity = 99, RowVersion = job.RowVersion });

        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        Assert.Equal(5m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
        Assert.Equal("InProgress", (await GetWorkOrderAsync(admin, job.Id))!.Status);
    }

    [Fact]
    public async Task Parts_can_only_change_while_the_job_is_in_progress()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var part = await CreateStockedPartAsync(admin, onHand: 10);

        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, user.Id);
        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technician, user.Id);
        var approved = await ApproveWorkOrderAsync(manager, (await CreateReportedWorkOrderAsync(admin, manager, technician, user.Id)).Id);

        var onAssigned = await PostAddPartAsync(technician, assigned.Id, part.Id, 1);
        var onCompleted = await PostAddPartAsync(technician, completed.Id, part.Id, 1);
        var onApproved = await PostAddPartAsync(technician, approved.Id, part.Id, 1);

        Assert.Equal(HttpStatusCode.Conflict, onAssigned.StatusCode);
        Assert.Contains("only be added while it is in progress", await DetailAsync(onAssigned));
        Assert.Equal(HttpStatusCode.Conflict, onCompleted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, onApproved.StatusCode);
        Assert.Contains("locked", await DetailAsync(onApproved)); // AC-08: the central lock guard
        Assert.Equal(10m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task A_stale_row_version_is_a_409_and_a_missing_one_is_a_400()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 10);
        await SaveDiagnosisAsync(technician, job.Id); // moves the job's RowVersion on

        var stale = await technician.PostAsJsonAsync($"/api/work-orders/{job.Id}/parts",
            new AddWorkOrderPartRequest { PartId = part.Id, Quantity = 1, RowVersion = job.RowVersion });
        var missing = await technician.PostAsJsonAsync($"/api/work-orders/{job.Id}/parts",
            new { partId = part.Id, quantity = 1m });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("changed by someone else", await DetailAsync(stale));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(10m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
    }

    [Theory]
    [InlineData(0, "greater than zero")]
    [InlineData(-1, "greater than zero")]
    [InlineData(1.0005, "decimal places")]
    public async Task An_invalid_quantity_is_a_400(double quantity, string expected)
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 10);

        var response = await PostAddPartAsync(technician, job.Id, part.Id, (decimal)quantity);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await DetailAsync(response));
    }

    [Fact]
    public async Task Unknown_and_inactive_parts_cannot_be_used()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var retired = await CreateStockedPartAsync(admin, onHand: 5);
        var edit = await admin.PutAsJsonAsync($"/api/parts/{retired.Id}", new UpdatePartRequest
        {
            PartNumber = retired.PartNumber, Name = retired.Name, Unit = retired.Unit, UnitCost = retired.UnitCost,
            MinimumStockLevel = retired.MinimumStockLevel, IsActive = false, RowVersion = retired.RowVersion
        });
        edit.EnsureSuccessStatusCode();

        var inactive = await PostAddPartAsync(technician, job.Id, retired.Id, 1);
        var unknown = await PostAddPartAsync(technician, job.Id, int.MaxValue, 1);

        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(5m, (await GetPartAsync(admin, retired.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task A_technician_can_search_only_the_active_catalogue()
    {
        var (admin, _, technician, _, _) = await JobInProgressAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var active = await CreatePartAsync(admin, name: $"Tech pick {marker}");
        var retired = await CreatePartAsync(admin, name: $"Tech retired {marker}");
        var edit = await admin.PutAsJsonAsync($"/api/parts/{retired.Id}", new UpdatePartRequest
        {
            PartNumber = retired.PartNumber, Name = retired.Name, Unit = retired.Unit, UnitCost = retired.UnitCost,
            MinimumStockLevel = retired.MinimumStockLevel, IsActive = false, RowVersion = retired.RowVersion
        });
        edit.EnsureSuccessStatusCode();

        // Asking for inactive parts is ignored for a technician.
        var parts = (await technician.GetFromJsonAsync<PagedResult<PartDto>>($"/api/parts?search={marker}&includeInactive=true"))!.Items;

        Assert.Equal(new[] { active.Id }, parts!.Select(p => p.Id));
    }

    [Fact]
    public async Task The_office_sees_the_parts_and_the_ledger_can_be_filtered_by_job()
    {
        var (admin, manager, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 5, unitCost: 12.40m);
        await AddPartAsync(technician, job.Id, part.Id, 1.5m);

        var seenByManager = (await GetWorkOrderAsync(manager, job.Id))!;
        var ledger = await JobLedgerAsync(manager, job.Id);

        Assert.Equal(18.60m, seenByManager.PartsTotal);
        Assert.Equal("Issue", Assert.Single(ledger).TransactionType);
    }

    // ---- PendingParts and resume ----

    [Fact]
    public async Task A_job_waits_for_parts_and_resumes_only_once_they_are_in_stock()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var belt = await CreateStockedPartAsync(admin, onHand: 2);

        var pause = await PostPendingPartsAsync(technician, job.Id, belt.Id, 6, "  Need 6 m of belting  ");
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        var pending = (await pause.Content.ReadFromJsonAsync<WorkOrderDto>())!;
        Assert.Equal("PendingParts", pending.Status);
        Assert.Equal("Need 6 m of belting", pending.PendingPartsReason);
        Assert.Equal(belt.Id, pending.PendingPartId);
        Assert.Equal(belt.PartNumber, pending.PendingPartNumber);
        Assert.Equal(6m, pending.PendingPartQuantity);
        Assert.NotNull(pending.PendingPartsAt);

        // While paused, parts cannot be added and the job cannot be completed.
        Assert.Equal(HttpStatusCode.Conflict, (await PostAddPartAsync(technician, job.Id, belt.Id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostCompleteAsync(technician, job.Id)).StatusCode);

        // Not enough stock yet: resume is refused and says what is missing.
        var early = await PostResumeAsync(technician, job.Id);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("Still waiting for parts: 6 pcs", await DetailAsync(early));

        await ReceiveAsync(admin, belt.Id, 4); // now exactly 6 on hand

        var resume = await PostResumeAsync(technician, job.Id);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var resumed = (await resume.Content.ReadFromJsonAsync<WorkOrderDto>())!;
        Assert.Equal("InProgress", resumed.Status);
        Assert.NotNull(resumed.ResumedAt);
        Assert.Equal(job.StartedAt, resumed.StartedAt); // resuming is not a restart
        Assert.Equal("Need 6 m of belting", resumed.PendingPartsReason); // kept as the record of the wait

        var used = await AddPartAsync(technician, job.Id, belt.Id, 6);
        Assert.Single(used.Parts);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task Waiting_for_a_part_that_is_already_in_stock_is_refused()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 3);

        var response = await PostPendingPartsAsync(technician, job.Id, part.Id, 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("add it to the job instead", await DetailAsync(response));
        Assert.Equal("InProgress", (await GetWorkOrderAsync(technician, job.Id))!.Status);
    }

    [Fact]
    public async Task Pending_parts_needs_a_reason_and_a_job_in_progress()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var part = await CreatePartAsync(admin);
        var inProgress = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, user.Id);

        var noReason = await PostPendingPartsAsync(technician, inProgress.Id, part.Id, 1, "   ");
        var notStarted = await PostPendingPartsAsync(technician, assigned.Id, part.Id, 1);

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, notStarted.StatusCode);
        Assert.Contains("Assigned", await DetailAsync(notStarted));
    }

    [Fact]
    public async Task Start_cannot_be_used_to_skip_the_resume_stock_guard()
    {
        var (admin, _, technician, _, job) = await JobInProgressAsync();
        var part = await CreatePartAsync(admin);
        await MarkPendingPartsAsync(technician, job.Id, part.Id, 1);

        var start = await PostStartAsync(technician, job.Id);
        var resumeAssigned = await PostResumeAsync(technician,
            (await CreateAssignedWorkOrderAsync(admin, await _factory.ClientWithRoleAsync(AppRoles.Manager), job.TechnicianId!)).Id);

        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Contains("Only an Assigned job can be started", await DetailAsync(start));
        Assert.Equal(HttpStatusCode.Conflict, resumeAssigned.StatusCode);
        Assert.Contains("Only a job waiting for parts can be resumed", await DetailAsync(resumeAssigned));
        Assert.Equal("PendingParts", (await GetWorkOrderAsync(technician, job.Id))!.Status);
    }

    [Fact]
    public async Task A_manager_can_cancel_a_job_that_is_waiting_for_parts()
    {
        var (admin, manager, technician, _, job) = await JobInProgressAsync();
        var part = await CreatePartAsync(admin);
        await MarkPendingPartsAsync(technician, job.Id, part.Id, 1);

        var cancelled = await CancelWorkOrderAsync(manager, job.Id, "Customer sourced the belt themselves");

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Approved", (await GetServiceRequestAsync(manager, job.ServiceRequestId))!.Status);
    }

    [Fact]
    public async Task Cancelling_a_job_returns_every_part_still_issued_to_it()
    {
        var (admin, manager, technician, _, job) = await JobInProgressAsync();
        var belt = await CreateStockedPartAsync(admin, onHand: 5);
        var oil = await CreateStockedPartAsync(admin, onHand: 10);
        await AddPartAsync(technician, job.Id, belt.Id, 2);
        await AddPartAsync(technician, job.Id, oil.Id, 1.5m);
        var withParts = await AddPartAsync(technician, job.Id, belt.Id, 1);
        Assert.Equal(3, withParts.Parts.Count);

        var cancelled = await CancelWorkOrderAsync(manager, job.Id, "Customer replaced the machine");

        // Stock is back where it started; the job (locked now) has no parts left on it.
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Empty(cancelled.Parts);
        Assert.Equal(5m, (await GetPartAsync(admin, belt.Id)).QuantityOnHand);
        Assert.Equal(10m, (await GetPartAsync(admin, oil.Id)).QuantityOnHand);

        // One Return row per line, each against the job and recorded as the manager's cancellation.
        var returns = (await JobLedgerAsync(admin, job.Id)).Where(t => t.TransactionType == "Return").ToList();
        Assert.Equal(new[] { 1m, 1.5m, 2m }, returns.Select(t => t.Quantity).Order());
        Assert.All(returns, t => Assert.Equal($"Returned when work order #{job.Id} was cancelled", t.Notes));
        Assert.All(returns, t => Assert.Equal("Test Manager", t.CreatedByName));
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task A_job_waiting_for_parts_still_counts_as_open_work_for_its_technician()
    {
        var (admin, manager, technician, user, job) = await JobInProgressAsync();
        var part = await CreatePartAsync(admin);
        await MarkPendingPartsAsync(technician, job.Id, part.Id, 1);

        var technicians = await manager.GetFromJsonAsync<List<TechnicianSummaryDto>>("/api/technicians");

        Assert.Equal(1, technicians!.Single(t => t.Id == user.Id).OpenJobCount);
    }
}
