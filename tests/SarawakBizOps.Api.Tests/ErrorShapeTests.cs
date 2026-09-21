using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Every 4xx must be RFC 7807 ProblemDetails (application/problem+json) so the
/// web client can rely on a single error shape.
/// </summary>
[Collection(ApiCollection.Name)]
public class ErrorShapeTests
{
    private readonly ApiFactory _factory;

    public ErrorShapeTests(ApiFactory factory) => _factory = factory;

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        return problem;
    }

    [Fact]
    public async Task Missing_token_returns_401_problem_details()
    {
        var response = await _factory.CreateClient().GetAsync("/api/customers");

        await ReadProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_returns_401_with_detail_message()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = ApiFactory.AdminEmail, Password = "definitely-wrong" });

        var problem = await ReadProblemAsync(response, HttpStatusCode.Unauthorized);

        Assert.Equal("Invalid email or password.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Invalid_body_returns_400_with_field_errors()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "not-an-email", Password = "" });

        var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);

        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.EnumerateObject().Any());
    }

    [Fact]
    public async Task Unknown_record_returns_404_problem_details()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var response = await client.GetAsync("/api/customers/999999");

        await ReadProblemAsync(response, HttpStatusCode.NotFound);
    }
}
