using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.ServiceRequests;

public interface IServiceRequestService
{
    Task<ServiceResult<IReadOnlyList<ServiceRequestDto>>> GetAllAsync(ServiceRequestFilter filter, CancellationToken ct);
    Task<ServiceRequestDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<ServiceResult<ServiceRequestDto>> CreateAsync(CreateServiceRequestRequest request, string actingUserId, CancellationToken ct);
    Task<ServiceResult<ServiceRequestDto>> ApproveAsync(int id, string actingUserId, byte[] rowVersion, CancellationToken ct);
    Task<ServiceResult<ServiceRequestDto>> RejectAsync(int id, string reason, string actingUserId, byte[] rowVersion, CancellationToken ct);

    /// <summary>
    /// A Manager may cancel a New or Approved request; the creator-side roles (ServiceStaff/Admin) may
    /// only undo their own mistake while it is still New (<paramref name="isManager"/> selects which).
    /// </summary>
    Task<ServiceResult<ServiceRequestDto>> CancelAsync(
        int id, string reason, string actingUserId, bool isManager, byte[] rowVersion, CancellationToken ct);
}
