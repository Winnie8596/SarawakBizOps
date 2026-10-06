using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SarawakBizOps.Api.ErrorHandling;

/// <summary>
/// Turns anything that escapes a controller into a clean ProblemDetails 500 instead of a stack trace — see design
/// doc Section 13 API rules ("Do not expose stack traces, secrets or internal database details"). Runs inside the
/// framework's <c>UseExceptionHandler</c>, which clears the half-written response first and handles client
/// disconnects itself.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Logged here: since .NET 10 the middleware stays quiet about an exception a handler reports as handled.
        _logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);

        // Once the status line and headers are on the wire nothing can replace them; writing a second body would
        // only corrupt the response. Let the framework abort the connection instead.
        if (context.Response.HasStarted)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Server error",
            Detail = "An unexpected error occurred. Please try again."
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }
}
