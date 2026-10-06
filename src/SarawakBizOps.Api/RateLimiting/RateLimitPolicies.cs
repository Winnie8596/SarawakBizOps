using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.Services.Ai;
using SarawakBizOps.Api.Services.Auth;

namespace SarawakBizOps.Api.RateLimiting;

/// <summary>
/// The app's rate limits (<c>[EnableRateLimiting]</c>), each a fixed one-minute window. A refusal is a 429
/// ProblemDetails with Retry-After and a message for that limit.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Per signed-in user, on the AI endpoints (Ai:PermitsPerMinute).</summary>
    public const string Ai = "ai";

    /// <summary>Per client IP address, on POST /api/auth/login (Auth:LoginPermitsPerMinute).</summary>
    public const string Login = "login";

    private static readonly Dictionary<string, string> Messages = new()
    {
        [Ai] = "You have asked the AI assistant too often. Wait a minute and try again.",
        [Login] = "Too many sign-in attempts from this address. Wait a minute and try again."
    };

    public static IServiceCollection AddAppRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }
                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = policy is not null && Messages.TryGetValue(policy, out var message)
                        ? message
                        : "Too many requests. Wait a minute and try again."
                }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
            };

            // Only authorised requests reach it (UseRateLimiter runs after UseAuthorization), so the user id is
            // always there.
            limiter.AddPolicy(Ai, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => Window(http.RequestServices.GetRequiredService<IOptions<AiOptions>>().Value.PermitsPerMinute)));

            // Per address, so guessing passwords across many accounts is slowed as well as Identity's per-account
            // lockout. UseForwardedHeaders has already put the real caller's address here, not nginx's.
            limiter.AddPolicy(Login, http => RateLimitPartition.GetFixedWindowLimiter(
                (http.Connection.RemoteIpAddress ?? IPAddress.None).ToString(),
                _ => Window(http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.LoginPermitsPerMinute)));
        });

    private static FixedWindowRateLimiterOptions Window(int permitsPerMinute) => new()
    {
        PermitLimit = Math.Max(1, permitsPerMinute),
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };
}
