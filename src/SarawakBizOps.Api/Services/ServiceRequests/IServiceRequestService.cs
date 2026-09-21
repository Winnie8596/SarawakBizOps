using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.ServiceRequests;

public interface IServiceRequestService
{
    Task<ServiceResult<IReadOnlyList<ServiceRequestDto>>> GetAllAsync(ServiceRequestFilter filter, CancellationToken ct);
    Task<ServiceRequestDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<ServiceResult<ServiceRequestDto>> CreateAsync(
        CreateServiceRequestRequest request, string actingUserId, CancellationToken ct);
    Task<ServiceResult<ServiceRequestDto>> ApproveAsync(int id, string actingUserId, CancellationToken ct);
    Task<ServiceResult<ServiceRequestDto>> RejectAsync(
        int id, string reason, string actingUserId, CancellationToken ct);
}
