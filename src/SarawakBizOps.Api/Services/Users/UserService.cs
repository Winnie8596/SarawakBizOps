using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Users;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Users;

public class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(UserManager<ApplicationUser> userManager, ApplicationDbContext db, ILogger<UserService> logger)
    {
        _userManager = userManager;
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(ct);

        // One query for all role names instead of one GetRolesAsync per user.
        var roleByUserId = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.Id
                select new { ur.UserId, RoleName = r.Name })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.UserId, x => x.RoleName ?? string.Empty, ct);

        return users
            .Select(u => ToDto(u, roleByUserId.GetValueOrDefault(u.Id, string.Empty)))
            .ToList();
    }

    public async Task<UserDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id);
        return user is null ? null : ToDto(user, await GetRoleAsync(user));
    }

    public async Task<ServiceResult<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        if (!AppRoles.All.Contains(request.Role))
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.Validation, InvalidRoleMessage);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName.Trim(),
            EmailConfirmed = true,
            IsActive = true
        };

        // The user row and its role must land together or not at all.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.Validation, Describe(created));
        }

        var roleAdded = await _userManager.AddToRoleAsync(user, request.Role);
        if (!roleAdded.Succeeded)
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.Validation, Describe(roleAdded));
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation("User {UserId} created with role {Role}", user.Id, request.Role);

        return ServiceResult<UserDto>.Ok(ToDto(user, request.Role));
    }

    public async Task<ServiceResult<UserDto>> UpdateAsync(
        string id, UpdateUserRequest request, string actingUserId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.NotFound, "User not found.");
        }

        var currentRole = await GetRoleAsync(user);
        var newRole = request.Role ?? currentRole;
        var newActive = request.IsActive ?? user.IsActive;

        if (!AppRoles.All.Contains(newRole))
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.Validation, InvalidRoleMessage);
        }

        var roleChanging = !string.Equals(newRole, currentRole, StringComparison.Ordinal);
        var deactivating = user.IsActive && !newActive;

        // An Admin who could demote or deactivate themselves could remove the
        // last Admin and leave nobody able to manage users. Only other Admins
        // may change an Admin's role or status, so at least one always remains.
        if (id == actingUserId && (roleChanging || deactivating))
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.Conflict,
                "You cannot change your own role or deactivate your own account.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            user.FullName = request.FullName.Trim();
        }
        user.IsActive = newActive;

        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return ServiceResult<UserDto>.Fail(ServiceErrorKind.Validation, Describe(updated));
        }

        if (roleChanging)
        {
            if (currentRole.Length > 0)
            {
                await _userManager.RemoveFromRoleAsync(user, currentRole);
            }
            await _userManager.AddToRoleAsync(user, newRole);
        }

        // Tokens carry the security stamp, so changing it revokes every token
        // already issued to this user: deactivation and role changes apply
        // immediately instead of when the JWT expires.
        if (roleChanging || deactivating)
        {
            await _userManager.UpdateSecurityStampAsync(user);
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation(
            "User {UserId} updated by {ActingUserId}: role {OldRole}->{NewRole}, active {IsActive}",
            id, actingUserId, currentRole, newRole, newActive);

        return ServiceResult<UserDto>.Ok(ToDto(user, newRole));
    }

    public async Task<ServiceResult> ResetPasswordAsync(string id, string newPassword, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return ServiceResult.Fail(ServiceErrorKind.NotFound, "User not found.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            return ServiceResult.Fail(ServiceErrorKind.Validation, Describe(result));
        }

        // A reset is usually done for someone locked out; lift any lockout too.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        _logger.LogInformation("Password for user {UserId} reset by an administrator", id);
        return ServiceResult.Ok();
    }

    private static string InvalidRoleMessage => $"Role must be one of: {string.Join(", ", AppRoles.All)}.";

    private async Task<string> GetRoleAsync(ApplicationUser user)
        => (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

    private static string Describe(IdentityResult result)
        => string.Join(" ", result.Errors.Select(e => e.Description));

    private static UserDto ToDto(ApplicationUser u, string role) => new()
    {
        Id = u.Id,
        FullName = u.FullName,
        Email = u.Email ?? string.Empty,
        Role = role,
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt
    };
}
