using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Auth;

/// <summary>
/// A signed-in session: the short-lived access token and the user (the response body), and the refresh token the
/// controller puts in the HttpOnly cookie.
/// </summary>
public sealed record AuthSession(LoginResponse Response, string RefreshToken, DateTime RefreshExpiresAtUtc);

public interface IAuthService
{
    /// <summary>Null for a wrong password and an unknown or inactive account alike (and in about the same time).</summary>
    Task<AuthSession?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token (rotation). Null when the token is
    /// unknown, expired, revoked, or its user was deactivated or had their role or password changed since sign-in.
    /// </summary>
    Task<AuthSession?> RefreshAsync(string? refreshToken, CancellationToken ct = default);

    /// <summary>Ends the session behind this refresh token. Unknown or empty tokens are ignored.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);

    /// <summary>The signed-in user, or null if the account no longer exists.</summary>
    Task<CurrentUserResponse?> GetCurrentUserAsync(string userId);

    /// <summary>
    /// Changes the caller's own password. Rotates the security stamp, so every
    /// token issued before the change (including the caller's) stops working.
    /// </summary>
    Task<ServiceResult> ChangePasswordAsync(string userId, ChangePasswordRequest request);
}
