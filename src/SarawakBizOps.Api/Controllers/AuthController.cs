using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.RateLimiting;
using SarawakBizOps.Api.Services.Auth;

namespace SarawakBizOps.Api.Controllers;

// Sessions: login returns a short-lived access token in the body (the web app keeps it in memory only) and sets
// the refresh token as an HttpOnly cookie scoped to /api/auth. The app calls refresh before the access token
// expires and on page load; logout revokes the session and clears the cookie.
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AuthOptions _options;

    public AuthController(IAuthService authService, IOptions<AuthOptions> options)
    {
        _authService = authService;
        _options = options.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var session = await _authService.LoginAsync(request, ct);
        if (session is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid email or password.");
        }

        SetRefreshCookie(session);
        return Ok(session.Response);
    }

    // Anonymous on purpose: the access token may already have expired. The cookie is the credential, and
    // SameSite=Strict keeps other sites from making the browser send it.
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var session = await _authService.RefreshAsync(Request.Cookies[AuthOptions.RefreshCookieName], ct);
        if (session is null)
        {
            ClearRefreshCookie();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized",
                detail: "Your session has ended. Please sign in again.");
        }

        SetRefreshCookie(session);
        return Ok(session.Response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _authService.LogoutAsync(Request.Cookies[AuthOptions.RefreshCookieName], ct);
        ClearRefreshCookie();
        return NoContent();
    }

    // Any signed-in user may change their own password. The security stamp rotates, so the
    // caller's current token and session stop working and they must sign in again with the new password.
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _authService.ChangePasswordAsync(userId, request);
        return result.Succeeded ? NoContent() : this.ToProblem(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await _authService.GetCurrentUserAsync(userId);
        return user is null ? Unauthorized() : Ok(user);
    }

    private CookieOptions RefreshCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = _options.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = AuthOptions.RefreshCookiePath,
        Expires = expires
    };

    private void SetRefreshCookie(AuthSession session)
        => Response.Cookies.Append(AuthOptions.RefreshCookieName, session.RefreshToken,
            RefreshCookieOptions(new DateTimeOffset(session.RefreshExpiresAtUtc, TimeSpan.Zero)));

    private void ClearRefreshCookie()
        => Response.Cookies.Delete(AuthOptions.RefreshCookieName, RefreshCookieOptions(null));
}
