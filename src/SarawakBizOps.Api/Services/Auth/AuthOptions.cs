namespace SarawakBizOps.Api.Services.Auth;

/// <summary>Session and sign-in settings (the "Auth" section; the access-token lifetime stays under "Jwt").</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The cookie that carries the refresh token. HttpOnly, so page scripts never see it.</summary>
    public const string RefreshCookieName = "sbo_refresh";

    /// <summary>Only the auth endpoints need the cookie, so the browser sends it nowhere else.</summary>
    public const string RefreshCookiePath = "/api/auth";

    /// <summary>How long a sign-in lasts at most, however often it is refreshed: about one working day.</summary>
    public int RefreshTokenHours { get; set; } = 12;

    /// <summary>
    /// Mark the cookie Secure. Browsers accept Secure cookies from http://localhost, so this stays on for the
    /// Docker demo too; turn it off only to serve the app over plain HTTP from another host name.
    /// </summary>
    public bool RefreshCookieSecure { get; set; } = true;

    /// <summary>Sign-in attempts allowed per client IP address per minute (on top of Identity's per-account lockout).</summary>
    public int LoginPermitsPerMinute { get; set; } = 20;
}
