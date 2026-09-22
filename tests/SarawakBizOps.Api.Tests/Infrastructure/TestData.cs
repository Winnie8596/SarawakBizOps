using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Customers;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.DTOs.WorkOrders;

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

    // ---- Phase 4: work orders. Each helper drives the real endpoints one step further, so a test
    // (or EndpointAccessTests' Prepare functions) can ask for exactly the state it needs. ----

    public static async Task<ServiceRequestDto> CreateApprovedRequestAsync(HttpClient admin, HttpClient manager)
    {
        var request = await CreateNewRequestAsync(admin);
        var response = await manager.PostAsync($"/api/service-requests/{request.Id}/approve", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
    }

    public static Task<HttpResponseMessage> PostAssignAsync(HttpClient manager, int requestId, string technicianId)
        => manager.PostAsJsonAsync($"/api/service-requests/{requestId}/assign",
            new AssignWorkOrderRequest { TechnicianId = technicianId });

    public static async Task<WorkOrderDto> AssignWorkOrderAsync(HttpClient manager, int requestId, string technicianId)
    {
        var response = await PostAssignAsync(manager, requestId, technicianId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    /// <summary>An Approved request, assigned to <paramref name="technicianId"/>: a fresh WorkOrder in Assigned.</summary>
    public static async Task<WorkOrderDto> CreateAssignedWorkOrderAsync(HttpClient admin, HttpClient manager, string technicianId)
    {
        var request = await CreateApprovedRequestAsync(admin, manager);
        return await AssignWorkOrderAsync(manager, request.Id, technicianId);
    }

    public static async Task<WorkOrderDto> StartWorkOrderAsync(HttpClient technician, int workOrderId)
    {
        var response = await technician.PostAsync($"/api/work-orders/{workOrderId}/start", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<WorkOrderDto> CreateInProgressWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, technicianId);
        return await StartWorkOrderAsync(technician, assigned.Id);
    }

    public static Task<HttpResponseMessage> PatchDiagnosisAsync(
        HttpClient technician, int workOrderId,
        string diagnosis = "Compressor relay is faulty.",
        string workPerformed = "Replaced the relay and tested a full cycle.")
        => technician.PatchAsJsonAsync($"/api/work-orders/{workOrderId}/diagnosis",
            new DiagnosisUpdateRequest { Diagnosis = diagnosis, WorkPerformed = workPerformed });

    public static async Task<WorkOrderDto> SaveDiagnosisAsync(
        HttpClient technician, int workOrderId,
        string diagnosis = "Compressor relay is faulty.",
        string workPerformed = "Replaced the relay and tested a full cycle.")
    {
        var response = await PatchDiagnosisAsync(technician, workOrderId, diagnosis, workPerformed);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    /// <summary>InProgress with diagnosis and work performed already saved: Complete will succeed next.</summary>
    public static async Task<WorkOrderDto> CreateReadyToCompleteWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var inProgress = await CreateInProgressWorkOrderAsync(admin, manager, technician, technicianId);
        return await SaveDiagnosisAsync(technician, inProgress.Id);
    }

    public static async Task<WorkOrderDto> CompleteWorkOrderAsync(HttpClient technician, int workOrderId)
    {
        var response = await technician.PostAsync($"/api/work-orders/{workOrderId}/complete", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<WorkOrderDto> CreateCompletedWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var ready = await CreateReadyToCompleteWorkOrderAsync(admin, manager, technician, technicianId);
        return await CompleteWorkOrderAsync(technician, ready.Id);
    }

    public static async Task<WorkOrderDto> ApproveWorkOrderAsync(HttpClient manager, int workOrderId)
    {
        var response = await manager.PostAsync($"/api/work-orders/{workOrderId}/approve", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }
}
