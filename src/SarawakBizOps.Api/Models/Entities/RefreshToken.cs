namespace SarawakBizOps.Api.Models.Entities;

/// <summary>
/// One sign-in session's refresh token. The browser holds the token itself in an HttpOnly cookie; the database keeps
/// only its SHA-256 hash, so a copy of this table cannot be used to sign in. Every refresh rotates it: the presented
/// row gets <see cref="RotatedAt"/> and a new row with the same <see cref="ExpiresAt"/> replaces it, so a session
/// ends at most RefreshTokenHours after its login however often it is refreshed.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    // The user's security stamp at sign-in. Deactivation, a role change or a password change rotates the stamp,
    // which ends every session, exactly as it already revokes access tokens (JwtUserValidator).
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RotatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
