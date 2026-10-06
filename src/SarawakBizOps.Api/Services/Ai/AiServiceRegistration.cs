using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Ai;

public static class AiServiceRegistration
{
    /// <summary>The per-user rate limit on the AI endpoints (<c>[EnableRateLimiting]</c>).</summary>
    public const string RateLimitPolicy = "ai";

    /// <summary>
    /// Registers the AI features from the "Ai" section. A misconfigured real provider stops startup with a hint (like
    /// the JWT key), rather than failing on the first question; the mock needs no configuration at all.
    /// </summary>
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.SectionName);
        services.Configure<AiOptions>(section);
        var options = section.Get<AiOptions>() ?? new AiOptions();

        if (string.Equals(options.Provider, AiProviders.OpenAICompatible, StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
                || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    "Ai:Provider is OpenAICompatible but Ai:BaseUrl is not an http(s) URL, e.g. " +
                    "https://generativelanguage.googleapis.com/v1beta/openai/ or http://localhost:11434/v1.");
            }
            if (string.IsNullOrWhiteSpace(options.Model))
            {
                throw new InvalidOperationException("Ai:Provider is OpenAICompatible but Ai:Model is not set.");
            }

            // The timeout that matters is AiService's (Ai:TimeoutSeconds); this one is only a backstop.
            services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(http =>
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds) + 10));
        }
        else if (string.Equals(options.Provider, AiProviders.Mock, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ILlmClient, MockLlmClient>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Ai:Provider '{options.Provider}' is not supported. Use {AiProviders.Mock} or {AiProviders.OpenAICompatible}.");
        }

        // A SELECT-only login when ConnectionStrings:AiReadOnly is set (docker-compose does), so SQL Server itself
        // refuses a write the ReadOnlyGuard somehow missed. Without it (LocalDB, tests) the guard is the only barrier.
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var readOnly = config.GetConnectionString(AiReadOnlyDataOptions.ConnectionStringName);
            return new AiReadOnlyDataOptions(string.IsNullOrWhiteSpace(readOnly)
                ? config.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.")
                : readOnly);
        });
        services.AddScoped<AiReadOnlyData>(); // Phase 14: also the Manager AI's analytics, over the guarded context
        services.AddScoped<TechnicianAssistantContextBuilder>();
        services.AddScoped<IAiService, AiService>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }
                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "You have asked the AI assistant too often. Wait a minute and try again."
                }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
            };

            // One fixed one-minute window per signed-in user. Only authorised requests reach it
            // (UseRateLimiter runs after UseAuthorization), so the user id is always there.
            limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, http.RequestServices.GetRequiredService<IOptions<AiOptions>>().Value.PermitsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });

        return services;
    }
}
