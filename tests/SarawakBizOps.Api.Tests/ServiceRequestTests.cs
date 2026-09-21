using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.History;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 3: request intake (PRD 6.3): create, list, approve, reject, BR-10.</summary>
[Collection(ApiCollection.Name)]
public class ServiceRequestTests
{
    private readonly ApiFactory _factory;

    public ServiceRequestTests(ApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    private static Task<HttpResponseMessage> RejectAsync(HttpClient manager, int id, string? reason)
        => manager.PostAsJsonAsync($"/api/service-requests/{id}/reject", new { reason });

    private static Task<ServiceRequestDto?> GetAsync(HttpClient client, int id)
        => client.GetFromJsonAsync<ServiceRequestDto>($"/api/service-requests/{id}");

    // ---- Happy path ----

    [Fact]
    public async Task Staff_raise_a_request_and_a_manager_approves_it()
    {
        var admin = await _factory.AdminClientAsync();
        var (staff, staffUser) = await _factory.ClientAndUserWithRoleAsync(AppRoles.ServiceStaff);
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var created = await PostServiceRequestAsync(staff, customer.Id, equipment.Id, "  Compressor trips out  ", "High");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var request = (await created.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
        Assert.Equal("New", request.Status);
        Assert.Equal("High", request.Priority);
        Assert.Equal("Compressor trips out", request.ProblemDescription);
        Assert.Equal(customer.CompanyName, request.CustomerName);
        Assert.Equal(equipment.SerialNumber, request.EquipmentSerialNumber);
        Assert.Equal(staffUser.Id, request.CreatedByUserId);
        Assert.Null(request.ApprovedAt);

        var approved = await manager.PostAsync($"/api/service-requests/{request.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var result = (await approved.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
        Assert.Equal("Approved", result.Status);
        Assert.Equal("Test Manager", result.ApprovedByName);
        Assert.NotNull(result.ApprovedAt);
        Assert.Null(result.RejectionReason);

        var reloaded = await GetAsync(manager, request.Id);
        Assert.Equal("Approved", reloaded!.Status);
    }

    [Fact]
    public async Task A_manager_rejects_a_request_with_a_reason()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);

        var response = await RejectAsync(manager, request.Id, "  Out of warranty; quote required first  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
        Assert.Equal("Rejected", result.Status);
        Assert.Equal("Out of warranty; quote required first", result.RejectionReason);
        Assert.Equal("Test Manager", result.RejectedByName);
        Assert.NotNull(result.RejectedAt);
        Assert.Null(result.ApprovedAt);
    }

    [Fact]
    public async Task Priority_defaults_to_Medium_when_omitted()
    {
        var admin = await _factory.AdminClientAsync();

        var request = await CreateNewRequestAsync(admin);

        Assert.Equal("Medium", request.Priority);
    }

    [Fact]
    public async Task The_creator_comes_from_the_token_not_from_the_request_body()
    {
        var admin = await _factory.AdminClientAsync();
        var (staff, staffUser) = await _factory.ClientAndUserWithRoleAsync(AppRoles.ServiceStaff);
        var someoneElse = (await admin.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.UserId;
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var response = await staff.PostAsJsonAsync("/api/service-requests", new
        {
            customerId = customer.Id,
            equipmentId = equipment.Id,
            problemDescription = "Leaks oil",
            createdByUserId = someoneElse,
            status = "Approved"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var request = (await response.Content.ReadFromJsonAsync<ServiceRequestDto>())!;
        Assert.Equal(staffUser.Id, request.CreatedByUserId);
        Assert.Equal("New", request.Status);
    }

    // ---- BR-10 and creation validation ----

    [Fact]
    public async Task Equipment_that_belongs_to_another_customer_is_rejected_and_nothing_is_saved()
    {
        var admin = await _factory.AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var other = await CreateCustomerAsync(admin);
        var othersEquipment = await CreateEquipmentAsync(admin, other.Id);

        var response = await PostServiceRequestAsync(admin, customer.Id, othersEquipment.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not belong", await DetailAsync(response));
        var forCustomer = await admin.GetFromJsonAsync<List<ServiceRequestDto>>($"/api/service-requests?customerId={customer.Id}");
        var forOther = await admin.GetFromJsonAsync<List<ServiceRequestDto>>($"/api/service-requests?customerId={other.Id}");
        Assert.Empty(forCustomer!);
        Assert.Empty(forOther!);
    }

    [Fact]
    public async Task Retired_equipment_cannot_be_serviced_but_inactive_and_under_maintenance_equipment_can()
    {
        var admin = await _factory.AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var retired = await CreateEquipmentAsync(admin, customer.Id, "Retired");
        var inactive = await CreateEquipmentAsync(admin, customer.Id, "Inactive");
        var underMaintenance = await CreateEquipmentAsync(admin, customer.Id, "UnderMaintenance");

        var retiredResponse = await PostServiceRequestAsync(admin, customer.Id, retired.Id);
        var inactiveResponse = await PostServiceRequestAsync(admin, customer.Id, inactive.Id);
        var maintenanceResponse = await PostServiceRequestAsync(admin, customer.Id, underMaintenance.Id);

        Assert.Equal(HttpStatusCode.BadRequest, retiredResponse.StatusCode);
        Assert.Contains("Retired", await DetailAsync(retiredResponse));
        Assert.Equal(HttpStatusCode.Created, inactiveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, maintenanceResponse.StatusCode);
    }

    [Fact]
    public async Task Unknown_customer_or_equipment_is_a_400()
    {
        var admin = await _factory.AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var noCustomer = await PostServiceRequestAsync(admin, 999_999, equipment.Id);
        var noEquipment = await PostServiceRequestAsync(admin, customer.Id, 999_999);

        Assert.Equal(HttpStatusCode.BadRequest, noCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noEquipment.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task A_blank_problem_description_is_rejected(string problem)
    {
        var admin = await _factory.AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var response = await PostServiceRequestAsync(admin, customer.Id, equipment.Id, problem);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_over_long_description_and_an_unknown_priority_are_rejected()
    {
        var admin = await _factory.AdminClientAsync();
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);

        var tooLong = await PostServiceRequestAsync(admin, customer.Id, equipment.Id, new string('x', 2001));
        var badName = await PostServiceRequestAsync(admin, customer.Id, equipment.Id, priority: "Critical");
        var numeric = await PostServiceRequestAsync(admin, customer.Id, equipment.Id, priority: "1");

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
    }

    // ---- Reject needs a reason ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejecting_without_a_reason_is_a_400_and_leaves_the_request_New(string? reason)
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);

        var response = await RejectAsync(manager, request.Id, reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("New", (await GetAsync(manager, request.Id))!.Status);
    }

    // ---- Invalid transitions -> 409 ----

    [Fact]
    public async Task Approving_twice_is_a_409_that_says_why()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);
        await manager.PostAsync($"/api/service-requests/{request.Id}/approve", null);

        var again = await manager.PostAsync($"/api/service-requests/{request.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var detail = await DetailAsync(again);
        Assert.Contains("Approved", detail);
        Assert.Contains("Only a New request", detail);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_decided_the_other_way()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var approved = await CreateNewRequestAsync(admin);
        var rejected = await CreateNewRequestAsync(admin);
        await manager.PostAsync($"/api/service-requests/{approved.Id}/approve", null);
        await RejectAsync(manager, rejected.Id, "Duplicate of an earlier request");

        var rejectApproved = await RejectAsync(manager, approved.Id, "Changed my mind");
        var approveRejected = await manager.PostAsync($"/api/service-requests/{rejected.Id}/approve", null);
        var rejectRejected = await RejectAsync(manager, rejected.Id, "Again");

        Assert.Equal(HttpStatusCode.Conflict, rejectApproved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, approveRejected.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejectRejected.StatusCode);
        Assert.Equal("Approved", (await GetAsync(manager, approved.Id))!.Status);
        var stillRejected = (await GetAsync(manager, rejected.Id))!;
        Assert.Equal("Rejected", stillRejected.Status);
        Assert.Equal("Duplicate of an earlier request", stillRejected.RejectionReason);
    }

    [Fact]
    public async Task Concurrent_decisions_on_one_request_have_exactly_one_winner()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var request = await CreateNewRequestAsync(admin);

        // Three approvals and three rejections race for the same New request.
        var attempts = Enumerable.Range(0, 3)
            .Select(_ => manager.PostAsync($"/api/service-requests/{request.Id}/approve", null))
            .Concat(Enumerable.Range(0, 3).Select(_ => RejectAsync(manager, request.Id, "Racing rejection")))
            .ToList();
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // The stored record is coherent: it carries exactly the winner's decision and nothing of the losers'.
        var final = (await GetAsync(manager, request.Id))!;
        if (final.Status == "Approved")
        {
            Assert.NotNull(final.ApprovedAt);
            Assert.Null(final.RejectionReason);
            Assert.Null(final.RejectedAt);
        }
        else
        {
            Assert.Equal("Rejected", final.Status);
            Assert.NotNull(final.RejectedAt);
            Assert.Null(final.ApprovedAt);
        }
    }

    [Fact]
    public async Task Unknown_ids_return_404()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);

        var get = await admin.GetAsync("/api/service-requests/999999");
        var approve = await manager.PostAsync("/api/service-requests/999999/approve", null);
        var reject = await RejectAsync(manager, 999999, "No such request");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
    }

    // ---- List and filters ----

    [Fact]
    public async Task The_list_filters_by_status_priority_and_customer_and_shows_newest_first()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);
        var first = await CreateServiceRequestAsync(admin, customer.Id, equipment.Id, "First fault", "Low");
        var second = await CreateServiceRequestAsync(admin, customer.Id, equipment.Id, "Second fault", "Urgent");
        var third = await CreateServiceRequestAsync(admin, customer.Id, equipment.Id, "Third fault", "Urgent");
        await manager.PostAsync($"/api/service-requests/{second.Id}/approve", null);
        var scope = $"/api/service-requests?customerId={customer.Id}";

        var all = await manager.GetFromJsonAsync<List<ServiceRequestDto>>(scope);
        var news = await manager.GetFromJsonAsync<List<ServiceRequestDto>>($"{scope}&status=New");
        var urgent = await manager.GetFromJsonAsync<List<ServiceRequestDto>>($"{scope}&priority=Urgent");
        var urgentNew = await manager.GetFromJsonAsync<List<ServiceRequestDto>>($"{scope}&status=New&priority=Urgent");

        Assert.Equal(new[] { third.Id, second.Id, first.Id }, all!.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { third.Id, first.Id }, news!.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { third.Id, second.Id }, urgent!.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { third.Id }, urgentNew!.Select(r => r.Id).ToArray());
    }

    [Theory]
    [InlineData("status=Done")]
    [InlineData("status=1")]
    [InlineData("priority=Critical")]
    public async Task An_unknown_filter_value_is_a_400_that_lists_the_valid_ones(string query)
    {
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);

        var response = await manager.GetAsync($"/api/service-requests?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be one of", await DetailAsync(response));
    }

    // ---- Customer / equipment history now shows real requests ----

    [Fact]
    public async Task Customer_and_equipment_history_show_requests_created_through_the_API()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var customer = await CreateCustomerAsync(admin);
        var equipment = await CreateEquipmentAsync(admin, customer.Id);
        var request = await CreateServiceRequestAsync(admin, customer.Id, equipment.Id, "Belt slipping", "High");
        await manager.PostAsync($"/api/service-requests/{request.Id}/approve", null);

        var customerHistory = await admin.GetFromJsonAsync<ServiceHistoryDto>($"/api/customers/{customer.Id}/history");
        var equipmentHistory = await admin.GetFromJsonAsync<ServiceHistoryDto>($"/api/equipment/{equipment.Id}/history");

        foreach (var history in new[] { customerHistory!, equipmentHistory! })
        {
            var item = Assert.Single(history.Items);
            Assert.Equal("ServiceRequest", item.Type);
            Assert.Equal(request.Id, item.Id);
            Assert.Equal("Approved", item.Status);
            Assert.Equal("High", item.Priority);
            Assert.Equal("Belt slipping", item.Summary);
        }
    }
}
