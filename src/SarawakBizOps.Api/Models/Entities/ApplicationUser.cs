using Microsoft.AspNetCore.Identity;

namespace SarawakBizOps.Api.Models.Entities;

/// <summary>
/// Identity owns authentication (password hashing, lockout, tokens).
/// This class only adds the business fields the app needs on top of IdentityUser.
/// Do NOT add PasswordHash or a Role enum here — see design doc Section 5.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
