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
/// Phase 4: the first job end to end (PRD 6.4) — assign, technician starts/diagnoses/completes,
/// Manager approves. AC-01, AC-02, AC-05, AC-07 and the full-workflow test all live here.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkOrderTests
{
    private readonly ApiFactory _factory;

    public WorkOrderTests(ApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    private static Task<WorkOrderDto?> GetWorkOrderAsync(HttpClient client, int id)
        => client.GetFromJsonAsync<WorkOrderDto>($"/api/work-orders/{id}");

    private static Task<ServiceRequestDto?> GetRequestAsync(HttpClient client, int id)
        => client.GetFromJsonAsync<ServiceRequestDto>($"/api/service-requests/{id}");

    // ---- AC-01: assign creates exactly one WorkOrder; request marked assigned ----

    [Fact]
    public async Task Assigning_an_approved_request_creates_one_work_order_and_marks_it_assigned()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var request = await CreateApprovedRequestAsync(admin, manager);

        var response = await PostAssignAsync(manager, request.Id, technician.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var workOrder = (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;
        Assert.Equal("Assigned", workOrder.Status);
        Assert.Equal(request.Id, workOrder.ServiceRequestId);
        Assert.Equal(technician.Id, workOrder.TechnicianId);
        Assert.Equal("Test Technician", workOrder.TechnicianName);
        Assert.NotNull(workOrder.AssignedAt);
        Assert.NotEmpty(workOrder.RowVersion);

        var reloadedRequest = await GetRequestAsync(manager, request.Id);
        Assert.Equal("Assigned", reloadedRequest!.Status);
    }

    [Fact]
    public async Task Assigning_the_same_request_twice_fails_on_the_second_attempt()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var (_, otherTechnician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var request = await CreateApprovedRequestAsync(admin, manager);
        await AssignWorkOrderAsync(manager, request.Id, technician.Id);

        var second = await PostAssignAsync(manager, request.Id, otherTechnician.Id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("Assigned", await DetailAsync(second));
        var workOrders = await manager.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders");
        Assert.Single(workOrders!, w => w.ServiceRequestId == request.Id);
    }

    [Fact]
    public async Task A_request_that_is_not_approved_cannot_be_assigned()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var newRequest = await CreateNewRequestAsync(admin);

        var response = await PostAssignAsync(manager, newRequest.Id, technician.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var detail = await DetailAsync(response);
        Assert.Contains("New", detail);
        Assert.Contains("Only an Approved request", detail);
    }

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.ServiceStaff)]
    [InlineData(AppRoles.WarehouseStaff)]
    public async Task Assigning_to_a_non_technician_or_inactive_technician_is_rejected(string wrongRole)
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, wrongUser) = await _factory.ClientAndUserWithRoleAsync(wrongRole);
        var request = await CreateApprovedRequestAsync(admin, manager);

        var response = await PostAssignAsync(manager, request.Id, wrongUser.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Technician", await DetailAsync(response));
    }

    [Fact]
    public async Task Two_managers_racing_to_assign_the_same_request_have_exactly_one_winner()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var techA = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var techB = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var request = await CreateApprovedRequestAsync(admin, manager);

        var attempts = new[]
        {
            PostAssignAsync(manager, request.Id, techA.User.Id),
            PostAssignAsync(manager, request.Id, techB.User.Id)
        };
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var workOrders = await manager.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders");
        Assert.Single(workOrders!, w => w.ServiceRequestId == request.Id);
    }

    // ---- AC-02: technician sees customer/equipment/problem/priority/status; ownership enforced ----

    [Fact]
    public async Task The_assigned_technician_sees_full_job_context_and_it_appears_in_their_job_list()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);
        var request = await CreateServiceRequestAsync(admin, customer.Id, equipment.Id, "Belt slipping under load", "High");
        await manager.PostAsync($"/api/service-requests/{request.Id}/approve", null);
        var workOrder = await AssignWorkOrderAsync(manager, request.Id, technician.Id);

        var detail = await GetWorkOrderAsync(technicianClient, workOrder.Id);

        Assert.Equal(customer.CompanyName, detail!.CustomerName);
        Assert.Equal(equipment.SerialNumber, detail.EquipmentSerialNumber);
        Assert.Equal(equipment.EquipmentType, detail.EquipmentType);
        Assert.Equal("Belt slipping under load", detail.ProblemDescription);
        Assert.Equal("High", detail.Priority);
        Assert.Equal("Assigned", detail.Status);

        var mine = await technicianClient.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders/mine");
        Assert.Single(mine!, w => w.Id == workOrder.Id);
    }

    [Fact]
    public async Task Another_technician_cannot_read_or_act_on_someone_elses_job()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, owner) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var (otherClient, _) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateAssignedWorkOrderAsync(admin, manager, owner.Id);

        var get = await otherClient.GetAsync($"/api/work-orders/{workOrder.Id}");
        var start = await otherClient.PostAsync($"/api/work-orders/{workOrder.Id}/start", null);
        var mine = await otherClient.GetFromJsonAsync<List<WorkOrderDto>>("/api/work-orders/mine");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        Assert.Empty(mine!);
    }

    [Fact]
    public async Task Office_roles_can_read_any_work_order()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var staff = await _factory.ClientWithRoleAsync(AppRoles.ServiceStaff);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);

        var asAdmin = await GetWorkOrderAsync(admin, workOrder.Id);
        var asStaff = await GetWorkOrderAsync(staff, workOrder.Id);

        Assert.Equal(workOrder.Id, asAdmin!.Id);
        Assert.Equal(workOrder.Id, asStaff!.Id);
    }

    // ---- Start ----

    [Fact]
    public async Task The_technician_starts_an_assigned_job()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);

        var started = await StartWorkOrderAsync(technicianClient, workOrder.Id);

        Assert.Equal("InProgress", started.Status);
        Assert.NotNull(started.StartedAt);
    }

    [Fact]
    public async Task Starting_a_job_twice_is_a_409()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateInProgressWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        var again = await technicianClient.PostAsync($"/api/work-orders/{workOrder.Id}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("InProgress", await DetailAsync(again));
    }

    // ---- Diagnosis ----

    [Fact]
    public async Task Diagnosis_can_only_be_saved_while_the_job_is_in_progress()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);

        var beforeStart = await PatchDiagnosisAsync(technicianClient, assigned.Id);

        Assert.Equal(HttpStatusCode.Conflict, beforeStart.StatusCode);

        await StartWorkOrderAsync(technicianClient, assigned.Id);
        var saved = await SaveDiagnosisAsync(technicianClient, assigned.Id, "Loose belt", "Retensioned the belt");

        Assert.Equal("Loose belt", saved.Diagnosis);
        Assert.Equal("Retensioned the belt", saved.WorkPerformed);
    }

    // ---- AC-05: completion rejected without diagnosis / work performed ----

    [Fact]
    public async Task Completing_without_diagnosis_or_work_performed_is_rejected()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateInProgressWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        var response = await technicianClient.PostAsync($"/api/work-orders/{workOrder.Id}/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("diagnosis", await DetailAsync(response));
        var reloaded = await GetWorkOrderAsync(technicianClient, workOrder.Id);
        Assert.Equal("InProgress", reloaded!.Status);
    }

    [Theory]
    [InlineData("Loose belt", "   ")]
    [InlineData("   ", "Retensioned the belt")]
    public async Task Saving_a_diagnosis_with_a_blank_field_is_rejected_and_the_job_still_cannot_complete(
        string diagnosis, string workPerformed)
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateInProgressWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        var patch = await PatchDiagnosisAsync(technicianClient, workOrder.Id, diagnosis, workPerformed);

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);

        var response = await technicianClient.PostAsync($"/api/work-orders/{workOrder.Id}/complete", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Completing_with_both_fields_recorded_succeeds()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateReadyToCompleteWorkOrderAsync(admin, manager, technicianClient, technician.Id);

        var completed = await CompleteWorkOrderAsync(technicianClient, workOrder.Id);

        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    // ---- AC-07: Manager approval locks the work order ----

    [Fact]
    public async Task Approving_a_completed_job_locks_it_against_further_technician_action()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateReadyToCompleteWorkOrderAsync(admin, manager, technicianClient, technician.Id);
        await CompleteWorkOrderAsync(technicianClient, workOrder.Id);

        var approved = await ApproveWorkOrderAsync(manager, workOrder.Id);

        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.ApprovedAt);

        var editAfterApproval = await PatchDiagnosisAsync(technicianClient, workOrder.Id, "Changed my mind", "Different work");
        var completeAgain = await technicianClient.PostAsync($"/api/work-orders/{workOrder.Id}/complete", null);

        Assert.Equal(HttpStatusCode.Conflict, editAfterApproval.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, completeAgain.StatusCode);
    }

    [Fact]
    public async Task Approving_a_job_that_is_not_completed_is_a_409()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (_, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var workOrder = await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);

        var response = await manager.PostAsync($"/api/work-orders/{workOrder.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Only a completed job can be approved", await DetailAsync(response));
    }

    // ---- Unknown ids ----

    [Fact]
    public async Task Unknown_work_order_ids_return_404()
    {
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, _) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/work-orders/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await technicianClient.PostAsync("/api/work-orders/999999/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsync("/api/work-orders/999999/approve", null)).StatusCode);
    }

    // ---- Technicians list ----

    [Fact]
    public async Task The_technicians_list_shows_open_job_counts_and_excludes_other_roles()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        await CreateAssignedWorkOrderAsync(admin, manager, technician.Id);
        await CreateInProgressWorkOrderAsync(admin, manager, technicianClient, technician.Id);
        await _factory.ClientAndUserWithRoleAsync(AppRoles.WarehouseStaff);

        var technicians = await manager.GetFromJsonAsync<List<TechnicianSummaryDto>>("/api/technicians");

        var listed = Assert.Single(technicians!, t => t.Id == technician.Id);
        Assert.Equal(2, listed.OpenJobCount);
    }

    // ---- The full tracer: request -> approve -> assign -> start -> complete -> approve ----

    [Fact]
    public async Task The_full_workflow_runs_end_to_end()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var staff = await _factory.ClientWithRoleAsync(AppRoles.ServiceStaff);
        var (technicianClient, technician) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var created = await CreateServiceRequestAsync(staff, customer.Id, equipment.Id, "Pump vibrates excessively", "Urgent");
        Assert.Equal("New", created.Status);

        var approveResponse = await manager.PostAsync($"/api/service-requests/{created.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var workOrder = await AssignWorkOrderAsync(manager, created.Id, technician.Id);
        Assert.Equal("Assigned", workOrder.Status);
        Assert.Equal("Assigned", (await GetRequestAsync(manager, created.Id))!.Status);

        var started = await StartWorkOrderAsync(technicianClient, workOrder.Id);
        Assert.Equal("InProgress", started.Status);

        var diagnosed = await SaveDiagnosisAsync(
            technicianClient, workOrder.Id, "Impeller is unbalanced", "Replaced the impeller and rebalanced the shaft");
        Assert.Equal("Impeller is unbalanced", diagnosed.Diagnosis);

        var completed = await CompleteWorkOrderAsync(technicianClient, workOrder.Id);
        Assert.Equal("Completed", completed.Status);

        var approved = await ApproveWorkOrderAsync(manager, workOrder.Id);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("Impeller is unbalanced", approved.Diagnosis);
        Assert.Equal("Replaced the impeller and rebalanced the shaft", approved.WorkPerformed);
        Assert.Equal(customer.CompanyName, approved.CustomerName);
        Assert.Equal(equipment.SerialNumber, approved.EquipmentSerialNumber);
        Assert.Equal("Urgent", approved.Priority);
    }
}
