using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Ai;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>Boots against an empty database with migrate-on-startup and demo seeding, as docker-compose does.</summary>
public class DemoSeedingTests : IClassFixture<DemoApiFactory>
{
    private readonly DemoApiFactory _factory;

    public DemoSeedingTests(DemoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Startup_on_an_empty_database_migrates_and_creates_one_account_per_role()
    {
        // The factory did not pre-migrate: reaching this point means MigrateOnStartup built the schema.
        // The test host sets an explicit admin password; compose relies on the demo-password fallback
        // (covered by SeedConfigurationTests and the compose smoke script).
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        Assert.Contains(AppRoles.Admin, admin.Roles);

        foreach (var demo in DemoSeeder.Users)
        {
            var login = await LoginAsync(demo.Email, DemoApiFactory.DemoPassword);
            Assert.Equal(new[] { demo.Role }, login.Roles);
        }

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in AppRoles.All)
        {
            Assert.Single(await users.GetUsersInRoleAsync(role));
        }
    }

    [Fact]
    public async Task Demo_data_covers_three_cities_and_every_role_can_be_used_by_the_api()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var customers = await response.Content.ReadFromJsonAsync<List<SarawakBizOps.Api.DTOs.Customers.CustomerDto>>();
        Assert.NotNull(customers);
        Assert.Equal(10, customers!.Count); // Phase 15: the PRD's ~10 customers
        foreach (var city in new[] { "Kuching", "Sibu", "Miri" })
        {
            Assert.Contains(customers, c => c.Address!.Contains(city));
        }
    }

    /// <summary>
    /// Phase 15 seed review (PRD §9): a reviewer can see every state without making one first. A work order is
    /// never New in practice (assigning creates it Assigned), so that is the one state the seed does not show.
    /// </summary>
    [Fact]
    public async Task Demo_data_has_a_record_in_every_reachable_state()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(Enum.GetValues<ServiceRequestStatus>().Order(),
            (await db.ServiceRequests.Select(r => r.Status).Distinct().ToListAsync()).Order());
        Assert.Equal(Enum.GetValues<WorkOrderStatus>().Where(s => s != WorkOrderStatus.New).Order(),
            (await db.WorkOrders.Select(w => w.Status).Distinct().ToListAsync()).Order());
        Assert.Equal(Enum.GetValues<EquipmentStatus>().Order(),
            (await db.Equipment.Select(e => e.Status).Distinct().ToListAsync()).Order());
    }

    [Fact]
    public async Task Running_the_demo_seeder_again_changes_nothing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await CountsAsync(db);
        var signaturesBefore = await SignatureKeysAsync(db);
        var photosBefore = await db.WorkOrderPhotos.CountAsync();
        var reportsBefore = await db.ServiceReports.AsNoTracking().Select(r => new { r.ReportNumber, r.Revision }).ToListAsync();

        await DemoSeeder.SeedAsync(scope.ServiceProvider);
        await DbSeeder.SeedAsync(scope.ServiceProvider);

        var after = await CountsAsync(db);
        Assert.Equal(before, after);
        Assert.Equal(signaturesBefore, await SignatureKeysAsync(db)); // no job is signed a second time
        Assert.Equal(12, signaturesBefore.Count); // the live Completed and Approved jobs, plus ten from the history
        Assert.Equal(5, photosBefore);
        Assert.Equal(photosBefore, await db.WorkOrderPhotos.CountAsync()); // no job gets its photos twice
        Assert.Equal(11, reportsBefore.Count); // every Approved job
        Assert.Equal(reportsBefore, await db.ServiceReports.AsNoTracking().Select(r => new { r.ReportNumber, r.Revision }).ToListAsync());
        Assert.Equal(10, after.Customers);
        Assert.Equal(30, after.Equipment);
        Assert.Equal(22, after.Requests); // 12 live + 10 behind the Phase 11 (and 13) history
        Assert.Equal(16, after.WorkOrders); // 6 live + 10 approved in earlier months
        Assert.Equal(12, after.Parts);
        Assert.Equal(19, after.Transactions); // 15 warehouse movements + 4 parts issued to jobs
    }

    private static async Task<(int Users, int Customers, int Equipment, int Requests, int WorkOrders, int Parts, int Transactions)>
        CountsAsync(ApplicationDbContext db)
        => (await db.Users.CountAsync(), await db.Customers.CountAsync(), await db.Equipment.CountAsync(),
            await db.ServiceRequests.CountAsync(), await db.WorkOrders.CountAsync(),
            await db.Parts.CountAsync(), await db.InventoryTransactions.CountAsync());

    private static async Task<List<string>> SignatureKeysAsync(ApplicationDbContext db)
        => await db.WorkOrders.AsNoTracking()
            .Where(w => w.SignatureBlobKey != null)
            .OrderBy(w => w.Id)
            .Select(w => w.SignatureBlobKey!)
            .ToListAsync();

    [Fact]
    public async Task Seeding_tolerates_service_requests_created_outside_the_seeder_that_share_a_natural_key()
    {
        // Mirrors compose-smoke.sh: it raises "Smoke test: approve me" on the same equipment on every
        // run, so a database re-used across two runs ends up with two ServiceRequest rows sharing the
        // (equipment, problem text) natural key SeedWorkOrdersAsync groups on. The next API start must
        // not crash seeding on that duplicate key.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var equipment = await db.Equipment.FirstAsync();
        var staff = await users.FindByEmailAsync("staff@sarawakbizops.local");

        // The database fixture is shared with the other tests in this class, so the two rows added
        // to reproduce the collision are removed again afterwards to leave their counts untouched.
        var duplicates = Enumerable.Range(0, 2)
            .Select(_ => new ServiceRequest
            {
                CustomerId = equipment.CustomerId,
                EquipmentId = equipment.Id,
                ProblemDescription = "Smoke test: approve me",
                Priority = RequestPriority.High,
                Status = ServiceRequestStatus.New,
                CreatedByUserId = staff!.Id
            })
            .ToList();
        db.ServiceRequests.AddRange(duplicates);
        await db.SaveChangesAsync();

        try
        {
            var exception = await Record.ExceptionAsync(() => DemoSeeder.SeedAsync(scope.ServiceProvider));
            Assert.Null(exception);
        }
        finally
        {
            db.ServiceRequests.RemoveRange(duplicates);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Demo_requests_cover_the_new_approved_assigned_and_rejected_states_for_the_manager()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var requests = await manager.GetFromJsonAsync<List<ServiceRequestDto>>("/api/service-requests");

        Assert.Equal(22, requests!.Count);
        Assert.Equal(3, requests.Count(r => r.Status == "New"));
        // One request is left Approved-but-unassigned so a reviewer always has one to assign by hand;
        // a second is Approved because its work order was cancelled (BR-12's revert-to-Approved); the
        // other Approved requests (five live, ten from the Phase 11 and 13 history) were carried into a WorkOrder and
        // are now Assigned.
        Assert.Equal(2, requests.Count(r => r.Status == "Approved"));
        Assert.Equal(15, requests.Count(r => r.Status == "Assigned"));
        var rejected = Assert.Single(requests, r => r.Status == "Rejected");
        Assert.False(string.IsNullOrWhiteSpace(rejected.RejectionReason));
        Assert.NotNull(rejected.RejectedByName);
        var cancelled = Assert.Single(requests, r => r.Status == "Cancelled");
        Assert.False(string.IsNullOrWhiteSpace(cancelled.CancellationReason));
        Assert.Equal("Aina Abdullah", cancelled.CancelledByName); // cancelled by its own creator (ServiceStaff)
        Assert.Contains(requests, r => r.Status == "New" && r.Priority == "Urgent");
        Assert.All(requests, r => Assert.Equal("Aina Abdullah", r.CreatedByName));
        // Every seeded request satisfies BR-10: its equipment belongs to its customer.
        foreach (var request in requests)
        {
            var equipment = await manager.GetFromJsonAsync<EquipmentDto>($"/api/equipment/{request.EquipmentId}");
            Assert.Equal(request.CustomerId, equipment!.CustomerId);
        }
    }

    [Fact]
    public async Task Demo_work_orders_cover_every_reachable_lifecycle_state()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);
        var technician = await _factory.CreateAuthenticatedClientAsync(
            "technician@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var workOrders = await manager.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders");

        Assert.Equal(16, workOrders!.Count);
        Assert.All(workOrders, w => Assert.Equal("Ahmad Bujang", w.TechnicianName));
        // Phase 11 (two more in Phase 13): ten jobs from earlier months, all approved, complete and signed, with no parts or photos.
        var history = workOrders.Where(w => w.CompletedAt < DateTime.UtcNow.AddDays(-20)).ToList();
        Assert.Equal(10, history.Count);
        Assert.All(history, w => Assert.Equal("Approved", w.Status));
        Assert.All(history, w => Assert.True(w.HasSignature && w.Diagnosis is not null && w.WorkPerformed is not null));
        Assert.All(history, w => Assert.True(w.AssignedAt < w.StartedAt && w.StartedAt < w.CompletedAt && w.CompletedAt < w.ApprovedAt));
        Assert.All(history, w => Assert.Empty(w.Parts));
        var assigned = Assert.Single(workOrders, w => w.Status == "Assigned");
        Assert.Null(assigned.Diagnosis);
        Assert.Empty(assigned.Parts);
        var inProgress = Assert.Single(workOrders, w => w.Status == "InProgress");
        Assert.False(string.IsNullOrWhiteSpace(inProgress.Diagnosis));
        Assert.False(string.IsNullOrWhiteSpace(inProgress.WorkPerformed));
        var relay = Assert.Single(inProgress.Parts);
        Assert.Equal(("REL-OVL-25A", 1m, 145.00m), (relay.PartNumber, relay.Quantity, relay.UnitPrice));
        var completed = Assert.Single(workOrders, w => w.Status == "Completed");
        Assert.NotNull(completed.CompletedAt);
        Assert.Null(completed.ApprovedAt);
        // The live Approved job, shown off with parts and photos; the others are the Phase 11 history.
        var approved = Assert.Single(workOrders, w => w.Status == "Approved" && w.EquipmentSerialNumber == "MOS-FLT-001"
            && w.ProblemDescription.StartsWith("Forklift hydraulic lift"));
        Assert.NotNull(approved.ApprovedAt);

        // Phase 8 (BR-03): exactly the Completed and Approved jobs carry the customer's signature, and it
        // really is in storage — the Manager can download a valid PNG. The InProgress job is left for the
        // demo technician to have signed.
        Assert.Equal(
            workOrders.Where(w => w.Status is "Completed" or "Approved").Select(w => w.Id).Order(),
            workOrders.Where(w => w.HasSignature).Select(w => w.Id).Order());
        Assert.All(workOrders.Where(w => w.HasSignature), w => Assert.NotNull(w.SignedAt));
        foreach (var signed in new[] { completed, approved })
        {
            var png = await manager.GetByteArrayAsync($"/api/work-orders/{signed.Id}/signature");
            Assert.Null(SarawakBizOps.Api.Services.WorkOrders.SignatureRules.Validate(png));
        }
        // Phase 9: the in-progress, completed and approved jobs carry fictional site photos, really in storage.
        Assert.Equal((1, 2, 2), (inProgress.Photos.Count, completed.Photos.Count, approved.Photos.Count));
        Assert.All(workOrders.Except(new[] { inProgress, completed, approved }), w => Assert.Empty(w.Photos));
        foreach (var photo in approved.Photos)
        {
            Assert.Equal("image/png", photo.ContentType);
            Assert.Equal("Ahmad Bujang", photo.UploadedByName);
            var bytes = await manager.GetByteArrayAsync($"/api/work-orders/{approved.Id}/photos/{photo.Id}");
            Assert.Equal((photo.ContentType, (string?)null), SarawakBizOps.Api.Services.WorkOrders.PhotoRules.Validate(bytes));
        }
        // Phase 10: every Approved job has its service report (locked by the approval), generated by the demo
        // Manager and really in storage; the Completed job is left for the demo Manager to generate one by hand.
        Assert.Null(completed.Report);
        Assert.All(workOrders.Where(w => w.Status != "Approved"), w => Assert.Null(w.Report));
        Assert.All(workOrders.Where(w => w.Status == "Approved"), w => Assert.True(w.Report is { IsLocked: true }));
        Assert.Matches(@"^SR-\d{4}-\d{6}$", approved.Report!.ReportNumber);
        Assert.Equal((1, true, "Jenny Lau"), (approved.Report.Revision, approved.Report.IsLocked, approved.Report.GeneratedByName));
        var pdf = await manager.GetByteArrayAsync($"/api/work-orders/{approved.Id}/report");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(approved.Report.SizeBytes, pdf.Length);
        Assert.Equal(approved.Report.ReportNumber, (await technician.GetFromJsonAsync<WorkOrderDto>($"/api/work-orders/{approved.Id}"))!.Report!.ReportNumber);

        Assert.Equal(new[] { "SEAL-KIT-HYD", "OIL-HYD-46" }.Order(), approved.Parts.Select(p => p.PartNumber).Order());
        Assert.Equal(363.80m, approved.PartsTotal); // 1 × RM 185.00 + 12 L × RM 14.90

        // PendingParts: waiting for belting that is not in stock, so it cannot be resumed yet.
        var pending = Assert.Single(workOrders, w => w.Status == "PendingParts");
        Assert.Equal("BLT-CNV-600", pending.PendingPartNumber);
        Assert.Equal(6m, pending.PendingPartQuantity);
        Assert.False(string.IsNullOrWhiteSpace(pending.PendingPartsReason));
        Assert.NotNull(pending.PendingPartsAt);
        Assert.Null(pending.ResumedAt);
        Assert.Equal("BRG-6205-2RS", Assert.Single(pending.Parts).PartNumber);
        var cancelled = Assert.Single(workOrders, w => w.Status == "Cancelled");
        Assert.False(string.IsNullOrWhiteSpace(cancelled.CancellationReason));
        Assert.Equal("Jenny Lau", cancelled.CancelledByName);
        // Cancelling reverted its request to Approved (BR-12's filtered unique index then allows a
        // second, live work order for the same request).
        var revertedRequest = await manager.GetFromJsonAsync<ServiceRequestDto>($"/api/service-requests/{cancelled.ServiceRequestId}");
        Assert.Equal("Approved", revertedRequest!.Status);

        // The demo Technician sees exactly their own jobs (BR-02) and can act on the assigned one.
        var mine = await technician.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders/mine");
        Assert.Equal(16, mine!.Count);
    }

    [Fact]
    public async Task Demo_parts_reconcile_with_their_ledger_and_four_active_parts_are_low_on_stock()
    {
        var warehouse = await _factory.CreateAuthenticatedClientAsync(
            "warehouse@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var active = await warehouse.GetFromJsonAsync<List<PartDto>>("/api/parts");
        var all = await warehouse.GetFromJsonAsync<List<PartDto>>("/api/parts?includeInactive=true");
        var lowStock = await warehouse.GetFromJsonAsync<List<PartDto>>("/api/parts/low-stock");
        var ledger = await warehouse.GetFromJsonAsync<List<InventoryTransactionDto>>("/api/inventory-transactions");

        Assert.Equal(11, active!.Count);
        Assert.Equal(12, all!.Count);
        // AC-09 groundwork: the four active parts at or below their minimum. The discontinued fuse is
        // below its minimum too but inactive, so it must stay off the list.
        Assert.Equal(
            new[] { "AVR-SX460", "BLD-BSW-900", "BLT-CNV-600", "SEAL-MECH-35" },
            lowStock!.Select(p => p.PartNumber).Order());
        Assert.Equal("BLT-CNV-600", lowStock![0].PartNumber); // 10 below its minimum: the most urgent
        Assert.DoesNotContain(lowStock, p => p.PartNumber == "FUSE-HRC-63");

        // 15 warehouse movements (Receive, or an Adjustment with a reason), plus one Issue per part line on a
        // job, raised by the technician and linked to that job (BR-05).
        Assert.Equal(19, ledger!.Count);
        var warehouseMovements = ledger.Where(t => t.TransactionType is "Receive" or "Adjustment").ToList();
        var issues = ledger.Where(t => t.TransactionType == "Issue").ToList();
        Assert.Equal(15, warehouseMovements.Count);
        Assert.Equal(4, issues.Count);
        Assert.All(warehouseMovements, t => Assert.Equal("Siti Nurul", t.CreatedByName));
        Assert.All(warehouseMovements, t => Assert.Null(t.WorkOrderId));
        Assert.All(ledger.Where(t => t.TransactionType == "Adjustment"), t => Assert.False(string.IsNullOrWhiteSpace(t.Notes)));
        Assert.All(issues, t => Assert.Equal("Ahmad Bujang", t.CreatedByName));
        Assert.All(issues, t => Assert.NotNull(t.WorkOrderId));
        Assert.All(issues, t => Assert.True(t.Quantity < 0));
        Assert.Equal(244.5m, all.Single(p => p.PartNumber == "OIL-HYD-46").QuantityOnHand); // 256.5 received less 12 used

        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task Dashboard_numbers_equal_the_hand_computed_values_of_the_seed()
    {
        // Phase 11 exit criterion. Counted by hand from the arrays in DemoSeeder:
        //   open requests — New: chiller (High), filling line A bottles (Medium), boiler (Urgent);
        //                   Approved: pump seal (High), filling line A belt (Medium, its job was cancelled);
        //   work orders   — the six live ones (one per status except New) plus ten approved in earlier months;
        //   low stock     — AVR-SX460, BLD-BSW-900, SEAL-MECH-35 and BLT-CNV-600 (the only one at zero).
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var summary = await manager.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary");

        Assert.Equal(new[] { ("Urgent", 1), ("High", 2), ("Medium", 2), ("Low", 0) },
            summary!.OpenRequestsByPriority.Select(c => (c.Key, c.Count)));
        Assert.Equal(
            new[] { ("New", 0), ("Assigned", 1), ("InProgress", 1), ("PendingParts", 1), ("Completed", 1), ("Approved", 11), ("Cancelled", 1) },
            summary.WorkOrdersByStatus.Select(c => (c.Key, c.Count)));
        Assert.Equal((4, 1), (summary.LowStock.Count, summary.LowStock.OutOfStock));
        Assert.Equal("BLT-CNV-600", summary.LowStock.Items[0].PartNumber);
        Assert.Equal(new[] { "AVR-SX460", "BLD-BSW-900", "BLT-CNV-600", "SEAL-MECH-35" },
            summary.LowStock.Items.Select(i => i.PartNumber).Order());
        Assert.Equal((5, 3, 1, 4),
            (summary.Kpis.OpenRequests, summary.Kpis.ActiveWorkOrders, summary.Kpis.AwaitingApproval, summary.Kpis.LowStockParts));

        // The trend depends on today's date (the seed is relative to it), so its expected value is computed here
        // independently: every seeded completion is within 140 days, so all twelve fall in the six-month window, each
        // in the Malaysia-time month of its CompletedAt.
        var jobs = await manager.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders");
        var expected = jobs!.Where(w => w.CompletedAt is not null)
            .GroupBy(w => w.CompletedAt!.Value.AddHours(8).ToString("yyyy-MM"))
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(12, expected.Values.Sum());
        Assert.Equal(6, summary.JobsCompletedPerMonth.Count);
        Assert.Equal(DateTime.UtcNow.AddHours(8).ToString("yyyy-MM"), summary.JobsCompletedPerMonth[^1].Month);
        Assert.All(summary.JobsCompletedPerMonth, m => Assert.Equal(expected.GetValueOrDefault(m.Month), m.Count));
        Assert.Equal(12, summary.JobsCompletedPerMonth.Sum(m => m.Count));
        Assert.Equal(summary.JobsCompletedPerMonth[^1].Count, summary.Kpis.CompletedThisMonth);
        Assert.True(summary.JobsCompletedPerMonth.Count(m => m.Count > 0) >= 4, "the seed spans several months");
    }

    [Fact]
    public async Task Phase_12_metrics_equal_the_hand_computed_values_of_the_seed()
    {
        // Phase 12 exit criterion. Counted by hand from the arrays in DemoSeeder; every seeded timestamp lies inside
        // the six-month period (the earliest, a request raised about 138 days ago).
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);
        var summary = (await manager.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary"))!;

        // Time to assignment, all 16 jobs: the ten earlier ones waited DaysToAssign = 0.5 + 0.1 + 2 + 1 + 2.5 + 1.5 +
        // 3 + 4 + 1 + 0.75 = 16.35 days; the live ones 2 (band saw) + 2 (compressor) + 2 (generator) + 2 (forklift) + 1
        // (filling line B) + 6 (the cancelled filling line A job) = 15 days. 31.35 days × 24 / 16 = 47.025 h.
        Assert.Equal(16, summary.AverageTimeToAssignment.Count);
        Assert.Equal(47.025, summary.AverageTimeToAssignment.AverageHours!.Value, 0.011);

        // Completion duration, the 12 completed jobs: DaysOnJob = 0.25 + 0.5 + 1 + 0.5 + 1 + 2 + 1 + 0.75 + 0.5 + 1.5
        // = 9 days, plus a day each for the generator and the forklift. 11 days × 24 / 12 = 22 h.
        Assert.Equal((22.0, 12), (summary.AverageCompletionDuration.AverageHours!.Value, summary.AverageCompletionDuration.Count));

        // Three or more fault reports: filling line A (capping head, bottle rejects, fraying belt — the last raised
        // 2 days ago), the compressor (overload trip, oil carry-over, short-cycling — 8 days ago) and the pump (hot
        // bearings, low flow, the leaking seal — 14 days ago). The kiln's second request was cancelled, so it is not a
        // fault; every other piece of equipment has at most two.
        Assert.Equal((3, 3), (summary.RepeatedFailures.MinimumRequests, summary.RepeatedFailures.EquipmentCount));
        Assert.Equal(new[] { ("SBW-FIL-001", 3), ("KCC-CMP-002", 3), ("SPO-PMP-002", 3) },
            summary.RepeatedFailures.Items.Select(i => (i.SerialNumber, i.RequestCount)));
        Assert.Equal("Santubong Bottling Works Sdn Bhd", summary.RepeatedFailures.Items[0].CustomerName);

        // Parts on jobs: seal kit 1 × 185.00, hydraulic oil 12 × 14.90, overload relay 1 × 145.00, bearings 2 × 18.40.
        Assert.Equal(
            new[] { ("SEAL-KIT-HYD", 1m, 185.00m), ("OIL-HYD-46", 12m, 178.80m), ("REL-OVL-25A", 1m, 145.00m), ("BRG-6205-2RS", 2m, 36.80m) },
            summary.TopPartsByUsage.Select(p => (p.PartNumber, p.Quantity, p.Cost)));
        Assert.All(summary.TopPartsByUsage, p => Assert.Equal(1, p.JobCount));

        // Warehouse: 11 active parts (the fuse is discontinued), the four low, the belt out; filling line B waits for
        // 6 m of belting with none on hand.
        var warehouse = await _factory.CreateAuthenticatedClientAsync(
            "warehouse@sarawakbizops.local", DemoApiFactory.DemoPassword);
        var stock = (await warehouse.GetFromJsonAsync<WarehouseDashboardDto>("/api/dashboard/summary"))!;
        Assert.Equal("Warehouse", stock.View);
        Assert.Equal((11, 4, 1, 1),
            (stock.Kpis.ActiveParts, stock.Kpis.LowStockParts, stock.Kpis.OutOfStockParts, stock.Kpis.JobsWaitingForParts));
        Assert.Equal(4, stock.LowStock.Items.Count);
        var wait = Assert.Single(stock.JobsWaitingForParts.Items);
        Assert.Equal(("BLT-CNV-600", 6m, 0m, false), (wait.PartNumber, wait.QuantityNeeded, wait.QuantityOnHand, wait.InStock));
        Assert.Equal(0, stock.JobsWaitingForParts.ReadyToResume);
        Assert.Equal(summary.TopPartsByUsage.Select(p => p.PartNumber), stock.TopPartsByUsage.Select(p => p.PartNumber));

        // Service queue: New — chiller (1 day), bottle rejects (2), boiler (3); Approved — pump seal (14), filling
        // line A belt (11, its job cancelled). The conveyor was rejected 19 days ago.
        var staff = await _factory.CreateAuthenticatedClientAsync(
            "staff@sarawakbizops.local", DemoApiFactory.DemoPassword);
        var queue = (await staff.GetFromJsonAsync<ServiceQueueDashboardDto>("/api/dashboard/summary"))!;
        Assert.Equal("ServiceQueue", queue.View);
        Assert.Equal((3, 2, 1), (queue.Kpis.AwaitingReview, queue.Kpis.AwaitingAssignment, queue.Kpis.RejectedRecently));
        Assert.Equal(new[] { "SPO-PMP-002", "SBW-FIL-001", "SPO-BLR-001", "SBW-FIL-001", "KCC-CHL-001" },
            queue.Queue.OldestOpen.Select(r => r.EquipmentSerialNumber));
        Assert.Equal("RTM-CNV-003", Assert.Single(queue.Queue.RecentlyRejected).EquipmentSerialNumber);

        // Cross-check the averages from the raw rows, independently of the SQL that computed them.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jobs = await db.WorkOrders.AsNoTracking().Include(w => w.ServiceRequest).ToListAsync();
        Assert.Equal(jobs.Average(w => (w.AssignedAt!.Value - w.ServiceRequest!.CreatedAt).TotalHours),
            summary.AverageTimeToAssignment.AverageHours!.Value, 0.01);
        Assert.Equal(jobs.Where(w => w.CompletedAt is not null).Average(w => (w.CompletedAt! - w.StartedAt!).Value.TotalHours),
            summary.AverageCompletionDuration.AverageHours!.Value, 0.01);
    }

    [Fact]
    public async Task The_assistant_on_the_demo_compressor_job_points_at_its_earlier_overload_trip()
    {
        // Phase 13 demo: the in-progress compressor job (short-cycling, overload relay) has two earlier jobs on the
        // same machine — an overload trip and an oil carry-over. The mock picks the related one and leaves the other.
        var technician = await _factory.CreateAuthenticatedClientAsync(
            "technician@sarawakbizops.local", DemoApiFactory.DemoPassword);
        var job = (await technician.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders/mine"))!
            .Single(w => w.Status == "InProgress");

        var response = await technician.PostAsJsonAsync("/api/ai/technician-assistant",
            new TechnicianAssistantRequest { WorkOrderId = job.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = (await response.Content.ReadFromJsonAsync<TechnicianAssistanceDto>())!;
        Assert.Equal(2, answer.PastCasesConsidered);
        Assert.Equal("Air compressor trips on overload during the hot afternoon shift.",
            Assert.Single(answer.RelevantPastCases).ProblemDescription);
        Assert.StartsWith("Motor protection tripping", answer.PossibleCauses[0].Cause);
        Assert.Contains(answer.RelevantParts, p => p.PartNumber == "REL-OVL-25A");
    }

    [Fact]
    public async Task The_dashboard_summary_answers_well_under_half_a_second_on_the_seed()
    {
        // NFR-03 sanity check (< 500 ms target), not a benchmark: the median of five warm, in-process calls.
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);
        (await manager.GetAsync("/api/dashboard/summary")).EnsureSuccessStatusCode(); // warm-up: JIT, query compilation

        var timings = new List<TimeSpan>();
        for (var i = 0; i < 5; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            (await manager.GetAsync("/api/dashboard/summary")).EnsureSuccessStatusCode();
            timings.Add(watch.Elapsed);
        }

        var median = timings.Order().ElementAt(2);
        Assert.True(median < TimeSpan.FromMilliseconds(500), $"median {median.TotalMilliseconds:F0} ms");
    }

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
