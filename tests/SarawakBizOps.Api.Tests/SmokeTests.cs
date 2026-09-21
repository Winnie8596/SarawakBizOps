using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

[Collection(ApiCollection.Name)]
public class SmokeTests
{
    private readonly ApiFactory _factory;

    public SmokeTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_public_and_return_no_data(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Seeded_admin_can_log_in_and_call_me()
    {
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = ApiFactory.AdminEmail, Password = ApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);

        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
