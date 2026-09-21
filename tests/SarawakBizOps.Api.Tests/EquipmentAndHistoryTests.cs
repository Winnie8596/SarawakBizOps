using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Customers;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.DTOs.History;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 1: finish Customers/Equipment (PRD §6.2) — PUT equipment and history endpoints.</summary>
[Collection(ApiCollection.Name)]
public class EquipmentAndHistoryTests
{
    private readonly ApiFactory _factory;

    public EquipmentAndHistoryTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> AdminClientAsync()
        => _factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    private async Task<HttpClient> ClientWithRoleAsync(string role)
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, role);
        return await _factory.CreateAuthenticatedClientAsync(user.Email, user.Password);
    }

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/customers",
            new CustomerRequest { CompanyName = $"Customer {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private static async Task<EquipmentDto> CreateEquipmentAsync(HttpClient client, int customerId, string? serial = null)
    {
        var response = await client.PostAsJsonAsync("/api/equipment", new EquipmentRequest
        {
            CustomerId = customerId,
            SerialNumber = serial ?? $"SN-{Guid.NewGuid():N}",
            EquipmentType = "Generator"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentDto>())!;
    }

    private static EquipmentUpdateRequest UpdateFor(EquipmentDto e, string? status = null) => new()
    {
        SerialNumber = e.SerialNumber,
        EquipmentType = e.EquipmentType,
        Status = status ?? e.Status
    };

    // ---- PUT /api/equipment/{id} ----

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.ServiceStaff)]
    public async Task Admin_and_ServiceStaff_can_update_equipment_including_status(string role)
    {
        var admin = await AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);
        var client = await ClientWithRoleAsync(role);

        var request = UpdateFor(equipment, EquipmentStatus.UnderMaintenance.ToString());
        request.Brand = "Cummins";
        request.Location = "Kuching depot";
        var response = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await admin.GetFromJsonAsync<EquipmentDto>($"/api/equipment/{equipment.Id}");
        Assert.Equal("UnderMaintenance", saved!.Status);
        Assert.Equal("Cummins", saved.Brand);
        Assert.Equal("Kuching depot", saved.Location);
    }

    [Theory]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.Technician)]
    [InlineData(AppRoles.WarehouseStaff)]
    public async Task Other_roles_get_403_when_updating_equipment(string role)
    {
        var admin = await AdminClientAsync();
        var equipment = await CreateEquipmentAsync(admin, (await CreateCustomerAsync(admin)).Id);
        var client = await ClientWithRoleAsync(role);

        var response = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}", UpdateFor(equipment));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_rejects_a_serial_number_used_by_other_equipment_but_allows_keeping_its_own()
    {
        var admin = await AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var first = await CreateEquipmentAsync(admin, customer.Id);
        var second = await CreateEquipmentAsync(admin, customer.Id);

        var clash = UpdateFor(second);
        clash.SerialNumber = first.SerialNumber;
        var clashResponse = await admin.PutAsJsonAsync($"/api/equipment/{second.Id}", clash);
        var ownSerialResponse = await admin.PutAsJsonAsync($"/api/equipment/{second.Id}", UpdateFor(second));

        Assert.Equal(HttpStatusCode.BadRequest, clashResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownSerialResponse.StatusCode);
    }

    [Fact]
    public async Task Update_rejects_an_unknown_status_and_missing_required_fields()
    {
        var admin = await AdminClientAsync();
        var equipment = await CreateEquipmentAsync(admin, (await CreateCustomerAsync(admin)).Id);

        var badStatus = await admin.PutAsJsonAsync($"/api/equipment/{equipment.Id}", UpdateFor(equipment, "Exploded"));
        var noSerial = UpdateFor(equipment);
        noSerial.SerialNumber = "";
        var missingField = await admin.PutAsJsonAsync($"/api/equipment/{equipment.Id}", noSerial);

        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingField.StatusCode);
    }

    [Fact]
    public async Task Update_of_unknown_equipment_returns_404()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync("/api/equipment/999999", new EquipmentUpdateRequest
        {
            SerialNumber = "x", EquipmentType = "x", Status = "Active"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_cannot_move_equipment_to_another_customer()
    {
        var admin = await AdminClientAsync();
        var owner = await CreateCustomerAsync(admin);
        var other = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, owner.Id);

        // A client that sneaks a customerId into the body must not be able to reassign ownership.
        var response = await admin.PutAsJsonAsync($"/api/equipment/{equipment.Id}", new
        {
            serialNumber = equipment.SerialNumber,
            equipmentType = equipment.EquipmentType,
            status = "Active",
            customerId = other.Id
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await admin.GetFromJsonAsync<EquipmentDto>($"/api/equipment/{equipment.Id}");
        Assert.Equal(owner.Id, saved!.CustomerId);
    }

    [Fact]
    public async Task Create_rejects_equipment_for_a_customer_that_does_not_exist()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/equipment", new EquipmentRequest
        {
            CustomerId = 999999, SerialNumber = $"SN-{Guid.NewGuid():N}", EquipmentType = "Pump"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- History endpoints ----

    [Fact]
    public async Task History_is_empty_for_a_customer_and_equipment_without_requests()
    {
        var admin = await AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var customerHistory = await admin.GetFromJsonAsync<ServiceHistoryDto>($"/api/customers/{customer.Id}/history");
        var equipmentHistory = await admin.GetFromJsonAsync<ServiceHistoryDto>($"/api/equipment/{equipment.Id}/history");

        Assert.Empty(customerHistory!.Items);
        Assert.Empty(equipmentHistory!.Items);
    }

    [Fact]
    public async Task History_lists_service_requests_newest_first_and_scopes_by_equipment()
    {
        var admin = await AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var pump = await CreateEquipmentAsync(admin, customer.Id);
        var generator = await CreateEquipmentAsync(admin, customer.Id);
        var adminId = (await admin.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.UserId;

        // Service-request endpoints arrive in Phase 3, so insert rows directly.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;
            db.ServiceRequests.AddRange(
                NewRequest(customer.Id, pump.Id, adminId, "Older pump fault", now.AddDays(-3)),
                NewRequest(customer.Id, pump.Id, adminId, "Newer pump fault", now.AddDays(-1)),
                NewRequest(customer.Id, generator.Id, adminId, "Generator fault", now.AddDays(-2)));
            await db.SaveChangesAsync();
        }

        var byCustomer = await admin.GetFromJsonAsync<ServiceHistoryDto>($"/api/customers/{customer.Id}/history");
        var byPump = await admin.GetFromJsonAsync<ServiceHistoryDto>($"/api/equipment/{pump.Id}/history");

        Assert.Equal(
            new[] { "Newer pump fault", "Generator fault", "Older pump fault" },
            byCustomer!.Items.Select(i => i.Summary).ToArray());
        Assert.Equal(
            new[] { "Newer pump fault", "Older pump fault" },
            byPump!.Items.Select(i => i.Summary).ToArray());
        Assert.All(byPump.Items, i => Assert.Equal("ServiceRequest", i.Type));
    }

    [Theory]
    [InlineData(AppRoles.Manager, HttpStatusCode.OK)]
    [InlineData(AppRoles.ServiceStaff, HttpStatusCode.OK)]
    [InlineData(AppRoles.Technician, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.WarehouseStaff, HttpStatusCode.Forbidden)]
    public async Task History_is_limited_to_office_roles(string role, HttpStatusCode expected)
    {
        var admin = await AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);
        var client = await ClientWithRoleAsync(role);

        var customerResponse = await client.GetAsync($"/api/customers/{customer.Id}/history");
        var equipmentResponse = await client.GetAsync($"/api/equipment/{equipment.Id}/history");

        Assert.Equal(expected, customerResponse.StatusCode);
        Assert.Equal(expected, equipmentResponse.StatusCode);
    }

    [Fact]
    public async Task History_of_unknown_customer_or_equipment_returns_404()
    {
        var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/customers/999999/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/equipment/999999/history")).StatusCode);
    }

    private static ServiceRequest NewRequest(int customerId, int equipmentId, string userId, string problem, DateTime createdAt) => new()
    {
        CustomerId = customerId,
        EquipmentId = equipmentId,
        CreatedByUserId = userId,
        ProblemDescription = problem,
        CreatedAt = createdAt
    };
}
