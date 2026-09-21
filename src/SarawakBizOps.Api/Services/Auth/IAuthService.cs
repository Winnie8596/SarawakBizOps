using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Auth;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    /// <summary>
    /// Changes the caller's own password. Rotates the security stamp, so every
    /// token issued before the change (including the caller's) stops working.
    /// </summary>
    Task<ServiceResult> ChangePasswordAsync(string userId, ChangePasswordRequest request);
}
