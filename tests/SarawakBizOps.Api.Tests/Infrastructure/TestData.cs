using System.Net.Http.Headers;
using System.Net.Http.Json;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Customers;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.DTOs.Parts;
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

    // ---- Phase 5: every mutating call now carries the RowVersion the caller last saw (the single
    // conflict-token transport). These helpers re-read the record right before acting so callers can
    // keep passing bare ids without tracking a DTO through every step of a chain. An id that does not
    // exist still needs *some* syntactically valid token so the request reaches the service layer and
    // comes back 404 (not a 400 for a missing rowVersion) — DummyRowVersion covers that case.
    private static readonly string DummyRowVersion = Convert.ToBase64String(new byte[8]);

    public static async Task<ServiceRequestDto?> GetServiceRequestAsync(HttpClient client, int id)
    {
        var response = await client.GetAsync($"/api/service-requests/{id}");
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ServiceRequestDto>() : null;
    }

    public static async Task<WorkOrderDto?> GetWorkOrderAsync(HttpClient client, int id)
    {
        var response = await client.GetAsync($"/api/work-orders/{id}");
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<WorkOrderDto>() : null;
    }

    public static async Task<HttpResponseMessage> PostApproveAsync(HttpClient manager, int requestId)
    {
        var current = await GetServiceRequestAsync(manager, requestId);
        return await manager.PostAsJsonAsync($"/api/service-requests/{requestId}/approve",
            new ApproveServiceRequestRequest { RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<HttpResponseMessage> PostRejectAsync(HttpClient manager, int requestId, string? reason)
    {
        var current = await GetServiceRequestAsync(manager, requestId);
        return await manager.PostAsJsonAsync($"/api/service-requests/{requestId}/reject",
            new { reason, rowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<HttpResponseMessage> PostCancelServiceRequestAsync(HttpClient client, int requestId, string? reason)
    {
        var current = await GetServiceRequestAsync(client, requestId);
        return await client.PostAsJsonAsync($"/api/service-requests/{requestId}/cancel",
            new { reason, rowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<ServiceRequestDto> CreateApprovedRequestAsync(HttpClient admin, HttpClient manager)
    {
        var request = await CreateNewRequestAsync(admin);
        var response = await PostApproveAsync(manager, request.Id);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
    }

    public static async Task<HttpResponseMessage> PostAssignAsync(HttpClient manager, int requestId, string technicianId)
    {
        var current = await GetServiceRequestAsync(manager, requestId);
        return await manager.PostAsJsonAsync($"/api/service-requests/{requestId}/assign",
            new AssignWorkOrderRequest { TechnicianId = technicianId, RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

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

    public static async Task<HttpResponseMessage> PostStartAsync(HttpClient technician, int workOrderId)
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.PostAsJsonAsync($"/api/work-orders/{workOrderId}/start",
            new RowVersionRequest { RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<WorkOrderDto> StartWorkOrderAsync(HttpClient technician, int workOrderId)
    {
        var response = await PostStartAsync(technician, workOrderId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<WorkOrderDto> CreateInProgressWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, technicianId);
        return await StartWorkOrderAsync(technician, assigned.Id);
    }

    public static async Task<HttpResponseMessage> PatchDiagnosisAsync(
        HttpClient technician, int workOrderId,
        string diagnosis = "Compressor relay is faulty.",
        string workPerformed = "Replaced the relay and tested a full cycle.")
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.PatchAsJsonAsync($"/api/work-orders/{workOrderId}/diagnosis",
            new DiagnosisUpdateRequest
            {
                Diagnosis = diagnosis,
                WorkPerformed = workPerformed,
                RowVersion = current?.RowVersion ?? DummyRowVersion
            });
    }

    public static async Task<WorkOrderDto> SaveDiagnosisAsync(
        HttpClient technician, int workOrderId,
        string diagnosis = "Compressor relay is faulty.",
        string workPerformed = "Replaced the relay and tested a full cycle.")
    {
        var response = await PatchDiagnosisAsync(technician, workOrderId, diagnosis, workPerformed);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    /// <summary>InProgress with diagnosis, work performed and the customer's signature (Phase 8, BR-03) already
    /// saved: Complete will succeed next.</summary>
    public static async Task<WorkOrderDto> CreateReadyToCompleteWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var inProgress = await CreateInProgressWorkOrderAsync(admin, manager, technician, technicianId);
        await SaveDiagnosisAsync(technician, inProgress.Id);
        return await UploadSignatureAsync(technician, inProgress.Id);
    }

    // ---- Phase 8: the customer's signature, uploaded as multipart/form-data like the web app's canvas ----

    /// <summary>A valid signature PNG (the same generator the demo seed uses).</summary>
    public static readonly byte[] SignaturePng = DemoSignatureImage.Create("test signature");

    public static MultipartFormDataContent SignatureForm(
        byte[]? file, string rowVersion, string fileName = "signature.png", string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        if (file is not null)
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, "file", fileName);
        }
        form.Add(new StringContent(rowVersion), "rowVersion");
        return form;
    }

    public static async Task<HttpResponseMessage> PostSignatureAsync(
        HttpClient technician, int workOrderId, byte[]? png = null,
        string fileName = "signature.png", string contentType = "image/png")
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        using var form = SignatureForm(png ?? SignaturePng, current?.RowVersion ?? DummyRowVersion, fileName, contentType);
        return await technician.PostAsync($"/api/work-orders/{workOrderId}/signature", form);
    }

    public static async Task<WorkOrderDto> UploadSignatureAsync(HttpClient technician, int workOrderId, byte[]? png = null)
    {
        var response = await PostSignatureAsync(technician, workOrderId, png);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    // ---- Phase 9: job photos, uploaded one per multipart request like the web app's queue ----

    /// <summary>A valid photo PNG (the same generator the demo seed uses).</summary>
    public static readonly byte[] PhotoPng = DemoPhotoImage.Create("test photo");

    /// <summary>The smallest thing that passes as a JPEG: the SOI marker and a JFIF APP0 header.</summary>
    public static readonly byte[] PhotoJpeg =
        { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9 };

    /// <summary>The smallest thing recognised as a WebP (a RIFF container whose form type is WEBP), which uploads refuse.</summary>
    public static readonly byte[] PhotoWebp =
        "RIFF"u8.ToArray().Concat(new byte[] { 0x1A, 0, 0, 0 }).Concat("WEBPVP8 "u8.ToArray()).Concat(new byte[18]).ToArray();

    public static MultipartFormDataContent PhotoForm(
        byte[]? file, string rowVersion, string fileName = "photo.jpg", string contentType = "image/jpeg")
        => SignatureForm(file, rowVersion, fileName, contentType);

    public static async Task<HttpResponseMessage> PostPhotoAsync(
        HttpClient technician, int workOrderId, byte[]? image = null,
        string fileName = "photo.png", string contentType = "image/png")
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        using var form = PhotoForm(image ?? PhotoPng, current?.RowVersion ?? DummyRowVersion, fileName, contentType);
        return await technician.PostAsync($"/api/work-orders/{workOrderId}/photos", form);
    }

    public static async Task<WorkOrderDto> UploadPhotoAsync(HttpClient technician, int workOrderId, byte[]? image = null)
    {
        var response = await PostPhotoAsync(technician, workOrderId, image);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<HttpResponseMessage> DeletePhotoAsync(HttpClient technician, int workOrderId, int photoId)
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/work-orders/{workOrderId}/photos/{photoId}")
        {
            Content = JsonContent.Create(new RowVersionRequest { RowVersion = current?.RowVersion ?? DummyRowVersion })
        });
    }

    public static async Task<HttpResponseMessage> PostCompleteAsync(HttpClient technician, int workOrderId)
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.PostAsJsonAsync($"/api/work-orders/{workOrderId}/complete",
            new RowVersionRequest { RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<WorkOrderDto> CompleteWorkOrderAsync(HttpClient technician, int workOrderId)
    {
        var response = await PostCompleteAsync(technician, workOrderId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<WorkOrderDto> CreateCompletedWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var ready = await CreateReadyToCompleteWorkOrderAsync(admin, manager, technician, technicianId);
        return await CompleteWorkOrderAsync(technician, ready.Id);
    }

    /// <summary>Completed and with its service report: ready to approve (approval requires the report).</summary>
    public static async Task<WorkOrderDto> CreateReportedWorkOrderAsync(
        HttpClient admin, HttpClient manager, HttpClient technician, string technicianId)
    {
        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technician, technicianId);
        return await GenerateReportAsync(manager, completed.Id);
    }

    public static async Task<HttpResponseMessage> PostApproveWorkOrderAsync(HttpClient manager, int workOrderId)
    {
        var current = await GetWorkOrderAsync(manager, workOrderId);
        return await manager.PostAsJsonAsync($"/api/work-orders/{workOrderId}/approve",
            new RowVersionRequest { RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<WorkOrderDto> ApproveWorkOrderAsync(HttpClient manager, int workOrderId)
    {
        var response = await PostApproveWorkOrderAsync(manager, workOrderId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<HttpResponseMessage> PostCancelWorkOrderAsync(HttpClient manager, int workOrderId, string? reason)
    {
        var current = await GetWorkOrderAsync(manager, workOrderId);
        return await manager.PostAsJsonAsync($"/api/work-orders/{workOrderId}/cancel",
            new { reason, rowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<WorkOrderDto> CancelWorkOrderAsync(HttpClient manager, int workOrderId, string reason = "Customer withdrew the job")
    {
        var response = await PostCancelWorkOrderAsync(manager, workOrderId, reason);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    // ---- Phase 10: the service report (Manager generates; no rowVersion — it documents the job without
    // changing it) ----

    public static Task<HttpResponseMessage> PostGenerateReportAsync(HttpClient manager, int workOrderId)
        => manager.PostAsync($"/api/work-orders/{workOrderId}/generate-report", null);

    public static async Task<WorkOrderDto> GenerateReportAsync(HttpClient manager, int workOrderId)
    {
        var response = await PostGenerateReportAsync(manager, workOrderId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    // ---- Phase 6: parts and the stock ledger ----

    public static async Task<HttpResponseMessage> PostPartAsync(
        HttpClient client, string? partNumber = null, decimal minimumStockLevel = 5, decimal unitCost = 25.50m,
        string unit = "pcs", string? name = null)
        => await client.PostAsJsonAsync("/api/parts", new CreatePartRequest
        {
            PartNumber = partNumber ?? $"PN-{Guid.NewGuid():N}"[..20],
            Name = name ?? $"Test part {Guid.NewGuid():N}",
            Unit = unit,
            UnitCost = unitCost,
            MinimumStockLevel = minimumStockLevel
        });

    public static async Task<PartDto> CreatePartAsync(
        HttpClient client, string? partNumber = null, decimal minimumStockLevel = 5, decimal unitCost = 25.50m,
        string unit = "pcs", string? name = null)
    {
        var response = await PostPartAsync(client, partNumber, minimumStockLevel, unitCost, unit, name);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PartDto>())!;
    }

    public static async Task<PartDto> GetPartAsync(HttpClient client, int id)
        => (await client.GetFromJsonAsync<PartDto>($"/api/parts/{id}"))!;

    public static Task<HttpResponseMessage> PostStockMovementAsync(
        HttpClient client, int partId, string type, decimal quantity, string? notes = null)
        => client.PostAsJsonAsync("/api/inventory-transactions", new CreateInventoryTransactionRequest
        {
            PartId = partId,
            TransactionType = type,
            Quantity = quantity,
            Notes = notes
        });

    public static async Task<InventoryTransactionDto> ReceiveAsync(
        HttpClient client, int partId, decimal quantity, string? notes = "Delivery note DN-TEST")
    {
        var response = await PostStockMovementAsync(client, partId, "Receive", quantity, notes);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InventoryTransactionDto>())!;
    }

    /// <summary>A new part with <paramref name="onHand"/> units already received through the ledger.</summary>
    public static async Task<PartDto> CreateStockedPartAsync(
        HttpClient client, decimal onHand, decimal minimumStockLevel = 5, decimal unitCost = 25.50m)
    {
        var part = await CreatePartAsync(client, minimumStockLevel: minimumStockLevel, unitCost: unitCost);
        if (onHand > 0)
        {
            await ReceiveAsync(client, part.Id, onHand);
        }
        return await GetPartAsync(client, part.Id);
    }

    // ---- Phase 7: parts on a job and PendingParts. Like the Phase 5 helpers, each re-reads the job for
    // its current RowVersion right before acting. ----

    public static async Task<HttpResponseMessage> PostAddPartAsync(
        HttpClient technician, int workOrderId, int partId, decimal quantity)
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.PostAsJsonAsync($"/api/work-orders/{workOrderId}/parts",
            new AddWorkOrderPartRequest { PartId = partId, Quantity = quantity, RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<WorkOrderDto> AddPartAsync(HttpClient technician, int workOrderId, int partId, decimal quantity)
    {
        var response = await PostAddPartAsync(technician, workOrderId, partId, quantity);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<HttpResponseMessage> DeletePartLineAsync(HttpClient technician, int workOrderId, int lineId)
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/work-orders/{workOrderId}/parts/{lineId}")
        {
            Content = JsonContent.Create(new RowVersionRequest { RowVersion = current?.RowVersion ?? DummyRowVersion })
        });
    }

    public static async Task<HttpResponseMessage> PostPendingPartsAsync(
        HttpClient technician, int workOrderId, int partId, decimal quantity, string reason = "Waiting for stock")
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.PostAsJsonAsync($"/api/work-orders/{workOrderId}/pending-parts",
            new PendingPartsRequest { Reason = reason, PartId = partId, Quantity = quantity, RowVersion = current?.RowVersion ?? DummyRowVersion });
    }

    public static async Task<WorkOrderDto> MarkPendingPartsAsync(HttpClient technician, int workOrderId, int partId, decimal quantity)
    {
        var response = await PostPendingPartsAsync(technician, workOrderId, partId, quantity);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    public static async Task<HttpResponseMessage> PostResumeAsync(HttpClient technician, int workOrderId)
    {
        var current = await GetWorkOrderAsync(technician, workOrderId);
        return await technician.PostAsJsonAsync($"/api/work-orders/{workOrderId}/resume",
            new RowVersionRequest { RowVersion = current?.RowVersion ?? DummyRowVersion });
    }
}
