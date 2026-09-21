using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Customers;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.DTOs.ServiceRequests;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// Creates uniquely-named records through the real endpoints, so tests never depend on run order
/// or on each other's data.
/// </summary>
public static class TestData
{
    public static Task<HttpClient> AdminClientAsync(this ApiFactory factory)
        => factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    /// <summary>Creates a fresh user with the role (through the Admin API) and returns a signed-in client.</summary>
    public static async Task<HttpClient> ClientWithRoleAsync(this ApiFactory factory, string role)
        => (await factory.ClientAndUserWithRoleAsync(role)).Client;

    public static async Task<(HttpClient Client, TestUser User)> ClientAndUserWithRoleAsync(
        this ApiFactory factory, string role)
    {
        var admin = await factory.AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, role);
        return (await factory.CreateAuthenticatedClientAsync(user.Email, user.Password), user);
    }

    public static async Task<CustomerDto> CreateCustomerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/customers",
            new CustomerRequest { CompanyName = $"Customer {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    /// <summary>Creates equipment; a non-Active status is applied afterwards through the update endpoint.</summary>
    public static async Task<EquipmentDto> CreateEquipmentAsync(HttpClient client, int customerId, string? status = null)
    {
        var response = await client.PostAsJsonAsync("/api/equipment", new EquipmentRequest
        {
            CustomerId = customerId,
            SerialNumber = $"SN-{Guid.NewGuid():N}",
            EquipmentType = "Generator"
        });
        response.EnsureSuccessStatusCode();
        var equipment = (await response.Content.ReadFromJsonAsync<EquipmentDto>())!;

        if (status is null || status == equipment.Status)
        {
            return equipment;
        }

        var update = await client.PutAsJsonAsync($"/api/equipment/{equipment.Id}", new EquipmentUpdateRequest
        {
            SerialNumber = equipment.SerialNumber,
            EquipmentType = equipment.EquipmentType,
            Status = status
        });
        update.EnsureSuccessStatusCode();
        return (await update.Content.ReadFromJsonAsync<EquipmentDto>())!;
    }

    public static async Task<HttpResponseMessage> PostServiceRequestAsync(
        HttpClient client, int customerId, int equipmentId,
        string problem = "Unit stops after a few minutes", string? priority = null)
        => await client.PostAsJsonAsync("/api/service-requests", new CreateServiceRequestRequest
        {
            CustomerId = customerId,
            EquipmentId = equipmentId,
            ProblemDescription = problem,
            Priority = priority
        });

    public static async Task<ServiceRequestDto> CreateServiceRequestAsync(
        HttpClient client, int customerId, int equipmentId,
        string problem = "Unit stops after a few minutes", string? priority = null)
    {
        var response = await PostServiceRequestAsync(client, customerId, equipmentId, problem, priority);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
    }

    /// <summary>A customer, one of its Active equipment items and a New request against them.</summary>
    public static async Task<ServiceRequestDto> CreateNewRequestAsync(HttpClient client)
    {
        var customer = await CreateCustomerAsync(client);
        var equipment = await CreateEquipmentAsync(client, customer.Id);
        return await CreateServiceRequestAsync(client, customer.Id, equipment.Id);
    }
}
