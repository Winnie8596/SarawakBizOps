using SarawakBizOps.Api.DTOs.Users;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct);
    Task<UserDto?> GetByIdAsync(string id, CancellationToken ct);
    Task<ServiceResult<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct);

    /// <param name="actingUserId">The Admin making the change (used to stop self-lockout).</param>
    Task<ServiceResult<UserDto>> UpdateAsync(string id, UpdateUserRequest request, string actingUserId, CancellationToken ct);

    Task<ServiceResult> ResetPasswordAsync(string id, string newPassword, CancellationToken ct);
}
