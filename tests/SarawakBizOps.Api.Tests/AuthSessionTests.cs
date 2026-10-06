using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Users;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Auth;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Sessions: a short-lived access token in the body, a rotating refresh token in an HttpOnly cookie, revocation on
/// logout and on anything that changes the security stamp, and the sign-in protections (per-address rate limit,
/// even timing for unknown accounts).
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthSessionTests
{
    private readonly ApiFactory _factory;

    public AuthSessionTests(ApiFactory factory) => _factory = factory;

    // The test server speaks http://localhost, where a cookie container would not send a Secure cookie, so these
    // tests carry the cookie by hand, exactly as the browser would to https://.
    private HttpClient NewClient(WebApplicationFactory<Program>? host = null)
        => (host ?? _factory).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<TestUser> NewUserAsync()
        => await TestUsers.CreateAsync(await _factory.AdminClientAsync(), AppRoles.ServiceStaff);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });

    private static string RefreshCookieOf(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthOptions.RefreshCookieName + "="));
        return header.Split(';')[0][(AuthOptions.RefreshCookieName.Length + 1)..];
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{AuthOptions.RefreshCookieName}={cookie}");
        }
        return client.SendAsync(request);
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Login_returns_a_short_lived_access_token_and_sets_the_refresh_token_as_a_locked_down_cookie()
    {
        var user = await NewUserAsync();
        var client = NewClient();

        var response = await LoginAsync(client, user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.InRange(body.ExpiresAtUtc - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthOptions.RefreshCookieName + "="));
        var attributes = cookie.ToLowerInvariant();
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/api/auth", attributes);
        Assert.DoesNotContain(RefreshCookieOf(response), await response.Content.ReadAsStringAsync());

        // Only a hash is stored: the database copy cannot be replayed.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        Assert.DoesNotContain(stored, t => t.TokenHash.Contains(RefreshCookieOf(response)));
        Assert.InRange(Assert.Single(stored).ExpiresAt - DateTime.UtcNow, TimeSpan.FromHours(11.9), TimeSpan.FromHours(12));
    }

    [Fact]
    public async Task Refresh_issues_a_new_access_token_and_rotates_the_cookie_without_extending_the_session()
    {
        var user = await NewUserAsync();
        var client = NewClient();
        var first = RefreshCookieOf(await LoginAsync(client, user.Email, user.Password));

        var refreshed = await RefreshAsync(client, first);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = (await refreshed.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(user.Email, body.Email);
        Assert.Equal(new[] { AppRoles.ServiceStaff }, body.Roles);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, body.Token));
        var second = RefreshCookieOf(refreshed);
        Assert.NotEqual(first, second);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.RefreshTokens.Where(t => t.UserId == user.Id).OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RotatedAt);
        Assert.Equal(rows[0].ExpiresAt, rows[1].ExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-token")]
    public async Task Refresh_without_a_valid_cookie_is_a_401_that_clears_the_cookie(string? cookie)
    {
        var response = await RefreshAsync(NewClient(), cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Your session has ended. Please sign in again.",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith(AuthOptions.RefreshCookieName + "=;") && c.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var user = await NewUserAsync();
        var client = NewClient();
        var cookie = RefreshCookieOf(await LoginAsync(client, user.Email, user.Password));

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", $"{AuthOptions.RefreshCookieName}={cookie}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, cookie)).StatusCode);
    }

    [Theory]
    [InlineData("deactivated")]
    [InlineData("role changed")]
    [InlineData("password changed")]
    public async Task A_security_stamp_change_ends_the_session_too(string change)
    {
        var admin = await _factory.AdminClientAsync();
        var user = await NewUserAsync();
        var client = NewClient();
        var login = await LoginAsync(client, user.Email, user.Password);
        var cookie = RefreshCookieOf(login);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        var changed = change switch
        {
            "deactivated" => await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { IsActive = false }),
            "role changed" => await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { Role = AppRoles.Manager }),
            _ => await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
                Content = JsonContent.Create(new ChangePasswordRequest { CurrentPassword = user.Password, NewPassword = "N3w-passw0rd" })
            })
        };
        changed.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, cookie)).StatusCode);
    }

    [Fact]
    public async Task A_rotated_token_is_accepted_briefly_for_a_second_tab_but_later_reuse_ends_every_session()
    {
        var user = await NewUserAsync();
        var client = NewClient();
        var first = RefreshCookieOf(await LoginAsync(client, user.Email, user.Password));
        var otherDevice = RefreshCookieOf(await LoginAsync(client, user.Email, user.Password));

        var second = RefreshCookieOf(await RefreshAsync(client, first));
        // Another tab sent the same cookie at the same moment: still a session, not an alarm.
        var sameMoment = await RefreshAsync(client, first);
        Assert.Equal(HttpStatusCode.OK, sameMoment.StatusCode);

        // Minutes later, the rotated token comes back: a copy is in someone else's hands.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(first)));
            await db.RefreshTokens.Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RotatedAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, first)).StatusCode);
        // Every session of the user ended, the legitimate ones included.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, second)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, RefreshCookieOf(sameMoment))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, otherDevice)).StatusCode);
        // Signing in again works.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_per_client_address()
    {
        var user = await NewUserAsync();
        await using var host = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:LoginPermitsPerMinute", "2");
            // The test server has no socket: take the caller's address from a test header, ahead of the pipeline.
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AddressFromHeader>());
        });
        var client = NewClient(host);

        Task<HttpResponseMessage> LoginFromAsync(string address, string password)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest { Email = user.Email, Password = password })
            };
            request.Headers.Add(AddressFromHeader.Header, address);
            return client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync("203.0.113.7", "wrong-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync("203.0.113.7", "wrong-2")).StatusCode);
        // Third attempt in the minute from that address: refused before the password is even checked.
        var limited = await LoginFromAsync("203.0.113.7", user.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("Too many sign-in attempts from this address. Wait a minute and try again.",
            (await limited.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        // Another address has its own allowance.
        Assert.Equal(HttpStatusCode.OK, (await LoginFromAsync("198.51.100.20", user.Password)).StatusCode);
    }

    [Theory]
    [InlineData("nobody-here@test.local")]
    [InlineData("inactive")]
    public async Task An_unknown_or_inactive_account_still_costs_a_password_hash(string email)
    {
        if (email == "inactive")
        {
            var user = await NewUserAsync();
            (await (await _factory.AdminClientAsync()).PatchAsJsonAsync($"/api/users/{user.Id}",
                new UpdateUserRequest { IsActive = false })).EnsureSuccessStatusCode();
            email = user.Email;
        }

        var hasher = new CountingHasher();
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher);
        }));

        var response = await LoginAsync(NewClient(host), email, "Whatever-1");

        // Same answer as a wrong password, and the same work: one verification, so timing does not tell them apart.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password.", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        Assert.Equal(1, hasher.Verifications);
    }

    private sealed class CountingHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _inner = new();
        private int _verifications;
        public int Verifications => _verifications;

        public string HashPassword(ApplicationUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private sealed class AddressFromHeader : IStartupFilter
    {
        public const string Header = "X-Test-Remote-Address";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[Header], out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
