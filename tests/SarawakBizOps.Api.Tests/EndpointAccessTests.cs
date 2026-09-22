using System.Net;
using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// AC-11: an unauthorized role is rejected by the API regardless of what the UI hides.
/// One table for the whole product: every phase that adds endpoints adds rows to <see cref="Endpoints"/>,
/// and each row is then exercised as anonymous and as every role, so a missing [Authorize] cannot slip through.
/// Each row's <c>Prepare</c> drives the real endpoints to whatever precondition its success case needs
/// (e.g. an Approved request for assign, an InProgress job for the diagnosis patch).
/// </summary>
[Collection(ApiCollection.Name)]
public class EndpointAccessTests
{
    private const string Anonymous = "Anonymous";

    private static readonly string[] Office = { AppRoles.Admin, AppRoles.Manager, AppRoles.ServiceStaff };

    /// <summary>Ids of the records an endpoint row's Path/Body can point at. RowVersion is the base64
    /// concurrency token of whichever record the row's Body posts back (Phase 5: required on every
    /// mutating call), left blank for rows that do not need one.</summary>
    private record Fixture(
        int CustomerId, int EquipmentId, int RequestId, int WorkOrderId, string TechnicianId, string RowVersion = "");

    /// <summary>Clients set up once per test row, for Prepare functions to drive through real endpoints.</summary>
    private record PrepContext(HttpClient Admin, HttpClient Manager, HttpClient TechnicianClient, string TechnicianId);

    private record Endpoint(
        string Name,
        HttpMethod Method,
        Func<Fixture, string> Path,
        Func<Fixture, object?> Body,
        string[] AllowedRoles,
        HttpStatusCode SuccessStatus,
        Func<PrepContext, Task<Fixture>> Prepare,
        // True when the endpoint's success case requires the caller to be the specific technician the
        // job is assigned to (BR-02): the Technician row then reuses PrepContext's technician client
        // instead of a freshly created, unrelated Technician.
        bool OwnedByTechnician = false);

    private static readonly Endpoint[] Endpoints =
    {
        // ---- Phase 3: service requests ----
        new("GET /api/service-requests", HttpMethod.Get,
            _ => "/api/service-requests", _ => null, Office, HttpStatusCode.OK,
            Prepare: async ctx => await NewRequestFixtureAsync(ctx)),

        new("GET /api/service-requests/{id}", HttpMethod.Get,
            f => $"/api/service-requests/{f.RequestId}", _ => null, Office, HttpStatusCode.OK,
            Prepare: async ctx => await NewRequestFixtureAsync(ctx)),

        new("POST /api/service-requests", HttpMethod.Post,
            _ => "/api/service-requests",
            f => new CreateServiceRequestRequest { CustomerId = f.CustomerId, EquipmentId = f.EquipmentId, ProblemDescription = "Access check" },
            new[] { AppRoles.Admin, AppRoles.ServiceStaff }, HttpStatusCode.Created,
            Prepare: async ctx =>
            {
                var customer = await CreateCustomerAsync(ctx.Admin);
                var equipment = await CreateEquipmentAsync(ctx.Admin, customer.Id);
                return new Fixture(customer.Id, equipment.Id, 0, 0, ctx.TechnicianId);
            }),

        new("POST /api/service-requests/{id}/approve", HttpMethod.Post,
            f => $"/api/service-requests/{f.RequestId}/approve", f => new ApproveServiceRequestRequest { RowVersion = f.RowVersion },
            new[] { AppRoles.Manager }, HttpStatusCode.OK,
            Prepare: async ctx => await NewRequestFixtureAsync(ctx)),

        new("POST /api/service-requests/{id}/reject", HttpMethod.Post,
            f => $"/api/service-requests/{f.RequestId}/reject",
            f => new RejectServiceRequestRequest { Reason = "Access check", RowVersion = f.RowVersion },
            new[] { AppRoles.Manager }, HttpStatusCode.OK,
            Prepare: async ctx => await NewRequestFixtureAsync(ctx)),

        // ServiceStaff/Admin can cancel their own New request; a Manager can cancel New or Approved.
        new("POST /api/service-requests/{id}/cancel", HttpMethod.Post,
            f => $"/api/service-requests/{f.RequestId}/cancel",
            f => new CancelServiceRequestRequest { Reason = "Access check", RowVersion = f.RowVersion },
            new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.ServiceStaff }, HttpStatusCode.OK,
            Prepare: async ctx => await NewRequestFixtureAsync(ctx)),

        // ---- Phase 4: assignment and work orders ----
        new("POST /api/service-requests/{id}/assign", HttpMethod.Post,
            f => $"/api/service-requests/{f.RequestId}/assign",
            f => new AssignWorkOrderRequest { TechnicianId = f.TechnicianId, RowVersion = f.RowVersion },
            new[] { AppRoles.Manager }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var request = await CreateApprovedRequestAsync(ctx.Admin, ctx.Manager);
                return new Fixture(request.CustomerId, request.EquipmentId, request.Id, 0, ctx.TechnicianId, request.RowVersion);
            }),

        new("GET /api/work-orders", HttpMethod.Get,
            _ => "/api/work-orders", _ => null, Office, HttpStatusCode.OK,
            Prepare: async ctx => new Fixture(0, 0, 0, 0, ctx.TechnicianId)),

        new("GET /api/work-orders/mine", HttpMethod.Get,
            _ => "/api/work-orders/mine", _ => null,
            new[] { AppRoles.Technician }, HttpStatusCode.OK,
            Prepare: async ctx => new Fixture(0, 0, 0, 0, ctx.TechnicianId)),

        new("GET /api/work-orders/{id}", HttpMethod.Get,
            f => $"/api/work-orders/{f.WorkOrderId}", _ => null,
            new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.ServiceStaff, AppRoles.Technician }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var wo = await CreateAssignedWorkOrderAsync(ctx.Admin, ctx.Manager, ctx.TechnicianId);
                return new Fixture(0, 0, 0, wo.Id, ctx.TechnicianId);
            },
            OwnedByTechnician: true),

        new("POST /api/work-orders/{id}/start", HttpMethod.Post,
            f => $"/api/work-orders/{f.WorkOrderId}/start", f => new RowVersionRequest { RowVersion = f.RowVersion },
            new[] { AppRoles.Technician }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var wo = await CreateAssignedWorkOrderAsync(ctx.Admin, ctx.Manager, ctx.TechnicianId);
                return new Fixture(0, 0, 0, wo.Id, ctx.TechnicianId, wo.RowVersion);
            },
            OwnedByTechnician: true),

        new("PATCH /api/work-orders/{id}/diagnosis", HttpMethod.Patch,
            f => $"/api/work-orders/{f.WorkOrderId}/diagnosis",
            f => new DiagnosisUpdateRequest { Diagnosis = "Access check", WorkPerformed = "Access check", RowVersion = f.RowVersion },
            new[] { AppRoles.Technician }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var wo = await CreateInProgressWorkOrderAsync(ctx.Admin, ctx.Manager, ctx.TechnicianClient, ctx.TechnicianId);
                return new Fixture(0, 0, 0, wo.Id, ctx.TechnicianId, wo.RowVersion);
            },
            OwnedByTechnician: true),

        new("POST /api/work-orders/{id}/complete", HttpMethod.Post,
            f => $"/api/work-orders/{f.WorkOrderId}/complete", f => new RowVersionRequest { RowVersion = f.RowVersion },
            new[] { AppRoles.Technician }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var wo = await CreateReadyToCompleteWorkOrderAsync(ctx.Admin, ctx.Manager, ctx.TechnicianClient, ctx.TechnicianId);
                return new Fixture(0, 0, 0, wo.Id, ctx.TechnicianId, wo.RowVersion);
            },
            OwnedByTechnician: true),

        new("POST /api/work-orders/{id}/approve", HttpMethod.Post,
            f => $"/api/work-orders/{f.WorkOrderId}/approve", f => new RowVersionRequest { RowVersion = f.RowVersion },
            new[] { AppRoles.Manager }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var wo = await CreateCompletedWorkOrderAsync(ctx.Admin, ctx.Manager, ctx.TechnicianClient, ctx.TechnicianId);
                return new Fixture(0, 0, 0, wo.Id, ctx.TechnicianId, wo.RowVersion);
            }),

        // Manager only, mirroring approve.
        new("POST /api/work-orders/{id}/cancel", HttpMethod.Post,
            f => $"/api/work-orders/{f.WorkOrderId}/cancel",
            f => new CancelWorkOrderRequest { Reason = "Access check", RowVersion = f.RowVersion },
            new[] { AppRoles.Manager }, HttpStatusCode.OK,
            Prepare: async ctx =>
            {
                var wo = await CreateAssignedWorkOrderAsync(ctx.Admin, ctx.Manager, ctx.TechnicianId);
                return new Fixture(0, 0, 0, wo.Id, ctx.TechnicianId, wo.RowVersion);
            }),

        new("GET /api/technicians", HttpMethod.Get,
            _ => "/api/technicians", _ => null,
            new[] { AppRoles.Manager }, HttpStatusCode.OK,
            Prepare: async ctx => new Fixture(0, 0, 0, 0, ctx.TechnicianId))
    };

    private static async Task<Fixture> NewRequestFixtureAsync(PrepContext ctx)
    {
        var request = await CreateNewRequestAsync(ctx.Admin);
        return new Fixture(request.CustomerId, request.EquipmentId, request.Id, 0, ctx.TechnicianId, request.RowVersion);
    }

    public static TheoryData<string, string> Rows()
    {
        var data = new TheoryData<string, string>();
        foreach (var endpoint in Endpoints)
        {
            data.Add(endpoint.Name, Anonymous);
            foreach (var role in AppRoles.All)
            {
                data.Add(endpoint.Name, role);
            }
        }
        return data;
    }

    private readonly ApiFactory _factory;

    public EndpointAccessTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Each_endpoint_allows_only_its_roles(string endpointName, string role)
    {
        var endpoint = Endpoints.Single(e => e.Name == endpointName);
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technicianClient, technicianUser) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);

        var fixture = await endpoint.Prepare(new PrepContext(admin, manager, technicianClient, technicianUser.Id));

        var client = role == Anonymous ? _factory.CreateClient()
            : endpoint.OwnedByTechnician && role == AppRoles.Technician ? technicianClient
            : await _factory.ClientWithRoleAsync(role);

        var message = new HttpRequestMessage(endpoint.Method, endpoint.Path(fixture));
        if (endpoint.Body(fixture) is { } body)
        {
            message.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(message);

        var expected = role == Anonymous ? HttpStatusCode.Unauthorized
            : endpoint.AllowedRoles.Contains(role) ? endpoint.SuccessStatus
            : HttpStatusCode.Forbidden;
        Assert.Equal(expected, response.StatusCode);
    }
}
