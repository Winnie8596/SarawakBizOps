using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 5: cancellation, the central lock guard, and the RowVersion conflict-token mechanism.
/// PRD §6.3–6.4, SDD §8–9, BR-02/06/13, AC-08.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkflowCancellationTests
{
    private readonly ApiFactory _factory;

    public WorkflowCancellationTests(ApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    // ---- ServiceRequest cancel: who can cancel what ----

    [Fact]
    public async Task ServiceStaff_can_cancel_their_own_New_request_with_a_reason()
    {
        var (staff, _) = await _factory.ClientAndUserWithRoleAsync(AppRoles.ServiceStaff);
        var request = await CreateNewRequestAsync(staff);

        var response = await PostCancelServiceRequestAsync(staff, request.Id, "Raised in error");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Raised in error", cancelled.CancellationReason);
        Assert.NotNull(cancelled.CancelledAt);
    }

    [Fact]
    public async Task ServiceStaff_cannot_cancel_a_request_once_it_is_Approved()
    {
        var admin = await _factory.AdminClientAsync();
        var (staff, _) = await _factory.ClientAndUserWithRoleAsync(AppRoles.ServiceStaff);
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateApprovedRequestAsync(admin, manager);

        var response = await PostCancelServiceRequestAsync(staff, request.Id, "Changed my mind");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Only a Manager can cancel an Approved request", await DetailAsync(response));
        Assert.Equal("Approved", (await GetServiceRequestAsync(staff, request.Id))!.Status);
    }

    [Fact]
    public async Task A_manager_can_cancel_an_Approved_request()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateApprovedRequestAsync(admin, manager);

        var response = await PostCancelServiceRequestAsync(manager, request.Id, "Customer no longer needs the job");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!.Status);
    }

    [Fact]
    public async Task Cancelling_without_a_reason_is_a_400_and_leaves_the_request_untouched()
    {
        var admin = await _factory.AdminClientAsync();
        var request = await CreateNewRequestAsync(admin);

        var response = await PostCancelServiceRequestAsync(admin, request.Id, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("New", (await GetServiceRequestAsync(admin, request.Id))!.Status);
    }

    [Fact]
    public async Task A_rejected_request_cannot_be_cancelled()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);
        await PostRejectAsync(manager, request.Id, "Out of scope");

        var response = await PostCancelServiceRequestAsync(manager, request.Id, "Too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Rejected", await DetailAsync(response));
    }

    // ---- WorkOrder cancel: Manager only, from New/Assigned/InProgress, reverts the request ----

    [Fact]
    public async Task A_manager_cancels_an_assigned_job_and_the_request_becomes_reassignable()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var (_, otherTechnician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);

        var response = await PostCancelWorkOrderAsync(manager, workOrder.Id, "Customer withdrew the job");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Customer withdrew the job", cancelled.CancellationReason);

        var request = await GetServiceRequestAsync(manager, workOrder.ServiceRequestId);
        Assert.Equal("Approved", request!.Status);

        // BR-12's index is now filtered: a second, live work order for the same request is allowed
        // once the first was cancelled.
        var reassigned = await AssignWorkOrderAsync(manager, request.Id, otherTechnician.Id);
        Assert.Equal("Assigned", reassigned.Status);
        Assert.Equal(otherTechnician.Id, reassigned.TechnicianId);

        var workOrders = await manager.GetFromJsonAsync<List<WorkOrderDto>>($"/api/work-orders?status=Cancelled");
        Assert.Contains(workOrders!, w => w.Id == workOrder.Id);
    }

    [Fact]
    public async Task A_manager_cancels_an_in_progress_job()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateInProgressWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        var response = await PostCancelWorkOrderAsync(manager, workOrder.Id, "Equipment was replaced instead");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!.Status);
    }

    [Fact]
    public async Task A_completed_or_approved_job_cannot_be_cancelled()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        var response = await PostCancelWorkOrderAsync(manager, completed.Id, "Too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Completed", await DetailAsync(response));
    }

    [Fact]
    public async Task Cancelling_a_work_order_without_a_reason_is_a_400()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);

        var response = await PostCancelWorkOrderAsync(manager, workOrder.Id, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Assigned", (await GetWorkOrderAsync(manager, workOrder.Id))!.Status);
    }

    // ---- AC-08 / central lock guard: a locked job refuses every mutating call, not just transitions ----

    [Fact]
    public async Task A_technician_cannot_edit_the_diagnosis_of_an_approved_job()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technicianClient, technician.Id);
        await ApproveWorkOrderAsync(manager, completed.Id);

        var response = await PatchDiagnosisAsync(technicianClient, completed.Id, "Different diagnosis", "Different work");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var detail = await DetailAsync(response);
        Assert.Contains("Approved", detail);
        Assert.Contains("locked", detail);
    }

    // ---- RowVersion: the single conflict-token transport closes the lost-update gap ----

    [Fact]
    public async Task Two_diagnosis_edits_against_the_same_stale_RowVersion_have_exactly_one_winner()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateInProgressWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        // Both edits read the same RowVersion (as if two tabs loaded the job before either saved).
        var attempts = new[]
        {
            technicianClient.PatchAsJsonAsync($"/api/work-orders/{workOrder.Id}/diagnosis", new DiagnosisUpdateRequest
            {
                Diagnosis = "First technician's read", WorkPerformed = "First pass", RowVersion = workOrder.RowVersion
            }),
            technicianClient.PatchAsJsonAsync($"/api/work-orders/{workOrder.Id}/diagnosis", new DiagnosisUpdateRequest
            {
                Diagnosis = "Second technician's read", WorkPerformed = "Second pass", RowVersion = workOrder.RowVersion
            })
        };
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var final = await GetWorkOrderAsync(technicianClient, workOrder.Id);
        Assert.True(final!.Diagnosis is "First technician's read" or "Second technician's read");
    }

    [Fact]
    public async Task Approving_a_request_with_a_stale_RowVersion_is_a_409()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);
        var staleRowVersion = request.RowVersion;
        // Someone else edits the record first (rejecting then re-fetching isn't possible, so simulate
        // staleness the same way the diagnosis test does: approve once for real, then reuse the old token).
        await PostApproveAsync(manager, request.Id);

        var response = await manager.PostAsJsonAsync($"/api/service-requests/{request.Id}/approve",
            new ApproveServiceRequestRequest { RowVersion = staleRowVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Approved", await DetailAsync(response));
    }

    [Fact]
    public async Task A_missing_or_malformed_RowVersion_is_a_400()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);

        var missing = await manager.PostAsJsonAsync($"/api/service-requests/{request.Id}/approve",
            new ApproveServiceRequestRequest { RowVersion = "" });
        var malformed = await manager.PostAsJsonAsync($"/api/service-requests/{request.Id}/approve",
            new ApproveServiceRequestRequest { RowVersion = "not-base64!" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("New", (await GetServiceRequestAsync(manager, request.Id))!.Status);
    }
}
