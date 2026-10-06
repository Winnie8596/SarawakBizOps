using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Behind nginx the API must see the caller's address, not the proxy's, and a caller must not be able to choose its
/// own by sending X-Forwarded-For. Only hops in ForwardedHeaders:KnownNetworks (the private ranges) are trusted.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReverseProxyTests
{
    private readonly ApiFactory _factory;

    public ReverseProxyTests(ApiFactory factory) => _factory = factory;

    [Theory]
    // nginx on the compose network passes the browser's address.
    [InlineData("172.18.0.5", "203.0.113.7", "203.0.113.7")]
    // Several proxies (e.g. a platform ingress in front of nginx): every private hop is skipped.
    [InlineData("10.0.0.4", "203.0.113.7, 10.0.0.9", "203.0.113.7")]
    // A forged entry in front of the real one stops at the first untrusted address: the real caller.
    [InlineData("172.18.0.5", "6.6.6.6, 203.0.113.7", "203.0.113.7")]
    // A caller that is not a known proxy cannot claim another address at all.
    [InlineData("198.51.100.1", "203.0.113.7", "198.51.100.1")]
    public async Task The_api_sees_the_real_client_address(string connectedFrom, string forwardedFor, string expected)
    {
        var seen = new SeenAddress();
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(seen);
            services.AddHttpContextAccessor();
            // The test server has no socket: pretend the TCP peer is connectedFrom, ahead of the whole pipeline.
            services.AddTransient<IStartupFilter>(_ => new ConnectFrom(IPAddress.Parse(connectedFrom)));
            // Runs inside UseAuthentication, i.e. after UseForwardedHeaders: records what the app sees.
            services.AddTransient<IClaimsTransformation, RecordAddress>();
        }));
        var token = await _factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = token.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        (await client.GetAsync("/api/customers")).EnsureSuccessStatusCode();

        Assert.Equal(IPAddress.Parse(expected), seen.Address);
    }

    private sealed class SeenAddress
    {
        public IPAddress? Address { get; set; }
    }

    private sealed class ConnectFrom(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class RecordAddress(IHttpContextAccessor http, SeenAddress seen) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            seen.Address = http.HttpContext?.Connection.RemoteIpAddress;
            return Task.FromResult(principal);
        }
    }
}
