using System.Net;
using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// AC-11: an unauthorized role is rejected by the API regardless of what the UI hides.
/// One table for the whole product: every phase that adds endpoints adds rows to <see cref="Endpoints"/>,
/// and each row is then exercised as anonymous and as every role, so a missing [Authorize] cannot slip through.
/// Setup always creates a fresh New request, so the "allowed" outcome is reachable on every run.
/// </summary>
[Collection(ApiCollection.Name)]
public class EndpointAccessTests
{
    private const string Anonymous = "Anonymous";

    /// <summary>Ids of the records the endpoint rows can point at.</summary>
    private record Fixture(int CustomerId, int EquipmentId, int RequestId);

    private record Endpoint(
        string Name,
        HttpMethod Method,
        Func<Fixture, string> Path,
        Func<Fixture, object?> Body,
        string[] AllowedRoles,
        HttpStatusCode SuccessStatus);

    private static readonly string[] Office = { AppRoles.Admin, AppRoles.Manager, AppRoles.ServiceStaff };

    // ---- Phase 3: service requests ----
    private static readonly Endpoint[] Endpoints =
    {
        new("GET /api/service-requests", HttpMethod.Get,
            _ => "/api/service-requests", _ => null, Office, HttpStatusCode.OK),

        new("GET /api/service-requests/{id}", HttpMethod.Get,
            f => $"/api/service-requests/{f.RequestId}", _ => null, Office, HttpStatusCode.OK),

        new("POST /api/service-requests", HttpMethod.Post,
            _ => "/api/service-requests",
            f => new CreateServiceRequestRequest { CustomerId = f.CustomerId, EquipmentId = f.EquipmentId, ProblemDescription = "Access check" },
            new[] { AppRoles.Admin, AppRoles.ServiceStaff }, HttpStatusCode.Created),

        new("POST /api/service-requests/{id}/approve", HttpMethod.Post,
            f => $"/api/service-requests/{f.RequestId}/approve", _ => null,
            new[] { AppRoles.Manager }, HttpStatusCode.OK),

        new("POST /api/service-requests/{id}/reject", HttpMethod.Post,
            f => $"/api/service-requests/{f.RequestId}/reject", _ => new RejectServiceRequestRequest { Reason = "Access check" },
            new[] { AppRoles.Manager }, HttpStatusCode.OK)
    };

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
        var request = await CreateNewRequestAsync(admin);
        var fixture = new Fixture(request.CustomerId, request.EquipmentId, request.Id);

        var client = role == Anonymous ? _factory.CreateClient() : await _factory.ClientWithRoleAsync(role);
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
