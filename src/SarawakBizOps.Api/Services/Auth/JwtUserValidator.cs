using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Services.Auth;

/// <summary>
/// A JWT is self-contained, so on its own it stays valid until it expires even
/// if the user is deactivated or demoted. This runs after the signature check on
/// every request and rejects tokens whose user no longer exists, is inactive, or
/// whose security stamp has changed since the token was issued.
/// Cost: one primary-key lookup per authenticated request.
/// </summary>
public static class JwtUserValidator
{
    public const string SecurityStampClaim = "stamp";

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();

        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(SecurityStampClaim);

        var user = userId is null ? null : await userManager.FindByIdAsync(userId);

        if (user is null || !user.IsActive || stamp is null || user.SecurityStamp != stamp)
        {
            context.Fail("The token is no longer valid.");
        }
    }
}
