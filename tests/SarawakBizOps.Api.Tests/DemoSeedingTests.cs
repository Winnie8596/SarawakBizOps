using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.DTOs.ServiceRequests;
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
        Assert.Equal(6, after.Requests);
    }

    private static async Task<(int Users, int Customers, int Equipment, int Requests)> CountsAsync(ApplicationDbContext db)
        => (await db.Users.CountAsync(), await db.Customers.CountAsync(),
            await db.Equipment.CountAsync(), await db.ServiceRequests.CountAsync());

    [Fact]
    public async Task Demo_requests_cover_the_new_approved_and_rejected_states_for_the_manager()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync(
            "manager@sarawakbizops.local", DemoApiFactory.DemoPassword);

        var requests = await manager.GetFromJsonAsync<List<ServiceRequestDto>>("/api/service-requests");

        Assert.Equal(6, requests!.Count);
        Assert.Equal(3, requests.Count(r => r.Status == "New"));
        Assert.Equal(2, requests.Count(r => r.Status == "Approved"));
        var rejected = Assert.Single(requests, r => r.Status == "Rejected");
        Assert.False(string.IsNullOrWhiteSpace(rejected.RejectionReason));
        Assert.NotNull(rejected.RejectedByName);
        Assert.Contains(requests, r => r.Status == "New" && r.Priority == "Urgent");
        Assert.All(requests, r => Assert.Equal("Aina Abdullah", r.CreatedByName));
        // Every seeded request satisfies BR-10: its equipment belongs to its customer.
        foreach (var request in requests)
        {
            var equipment = await manager.GetFromJsonAsync<EquipmentDto>($"/api/equipment/{request.EquipmentId}");
            Assert.Equal(request.CustomerId, equipment!.CustomerId);
        }
    }

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
