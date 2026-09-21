using SarawakBizOps.Api.DTOs.Auth;

namespace SarawakBizOps.Api.Services.Auth;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}
