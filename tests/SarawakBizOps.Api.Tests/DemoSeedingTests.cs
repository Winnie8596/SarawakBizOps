using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Equipment;
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
        Assert.Equal(5, customers!.Count);
        foreach (var city in new[] { "Kuching", "Sibu", "Miri" })
        {
            Assert.Contains(customers, c => c.Address!.Contains(city));
        }
    }

    [Fact]
    public async Task Running_the_demo_seeder_again_changes_nothing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await CountsAsync(db);

        await DemoSeeder.SeedAsync(scope.ServiceProvider);
        await DbSeeder.SeedAsync(scope.ServiceProvider);

        var after = await CountsAsync(db);
        Assert.Equal(before, after);
        Assert.Equal(5, after.Customers);
        Assert.Equal(10, after.Equipment);
        Assert.Equal(11, after.Requests);
        Assert.Equal(5, after.WorkOrders);
    }

    private static async Task<(int Users, int Customers, int Equipment, int Requests, int WorkOrders)> CountsAsync(ApplicationDbContext db)
        => (await db.Users.CountAsync(), await db.Customers.CountAsync(), await db.Equipment.CountAsync(),
            await db.ServiceRequests.CountAsync(), await db.WorkOrders.CountAsync());

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

        Assert.Equal(11, requests!.Count);
        Assert.Equal(3, requests.Count(r => r.Status == "New"));
        // One request is left Approved-but-unassigned so a reviewer always has one to assign by hand;
        // a second is Approved because its work order was cancelled (BR-12's revert-to-Approved); the
        // other four Approved requests were carried into a WorkOrder and are now Assigned.
        Assert.Equal(2, requests.Count(r => r.Status == "Approved"));
        Assert.Equal(4, requests.Count(r => r.Status == "Assigned"));
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
    public async Task Demo_work_orders_cover_every_phase_4_lifecycle_state()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);
        var technician = await _factory.CreateAuthenticatedClientAsync(
            "technician@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var workOrders = await manager.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders");

        Assert.Equal(5, workOrders!.Count);
        Assert.All(workOrders, w => Assert.Equal("Ahmad Bujang", w.TechnicianName));
        var assigned = Assert.Single(workOrders, w => w.Status == "Assigned");
        Assert.Null(assigned.Diagnosis);
        var inProgress = Assert.Single(workOrders, w => w.Status == "InProgress");
        Assert.False(string.IsNullOrWhiteSpace(inProgress.Diagnosis));
        Assert.False(string.IsNullOrWhiteSpace(inProgress.WorkPerformed));
        var completed = Assert.Single(workOrders, w => w.Status == "Completed");
        Assert.NotNull(completed.CompletedAt);
        Assert.Null(completed.ApprovedAt);
        var approved = Assert.Single(workOrders, w => w.Status == "Approved");
        Assert.NotNull(approved.ApprovedAt);
        var cancelled = Assert.Single(workOrders, w => w.Status == "Cancelled");
        Assert.False(string.IsNullOrWhiteSpace(cancelled.CancellationReason));
        Assert.Equal("Jenny Lau", cancelled.CancelledByName);
        // Cancelling reverted its request to Approved (BR-12's filtered unique index then allows a
        // second, live work order for the same request).
        var revertedRequest = await manager.GetFromJsonAsync<ServiceRequestDto>($"/api/service-requests/{cancelled.ServiceRequestId}");
        Assert.Equal("Approved", revertedRequest!.Status);

        // The demo Technician sees exactly their own jobs (BR-02) and can act on the assigned one.
        var mine = await technician.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders/mine");
        Assert.Equal(5, mine!.Count);
    }

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
