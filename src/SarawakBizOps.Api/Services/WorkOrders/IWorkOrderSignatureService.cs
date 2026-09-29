using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>Phase 8: the customer's signature on a job (BR-03, AC-06), stored through <see cref="IFileStorage"/>.</summary>
public interface IWorkOrderSignatureService
{
    /// <summary>
    /// Stores <paramref name="png"/> as the job's signature, replacing any earlier one (the customer signs
    /// again). Assigned technician only (BR-02), and only while the job is InProgress; carries and bumps the
    /// job's rowVersion like every other mutating call.
    /// </summary>
    Task<ServiceResult<WorkOrderDto>> SaveAsync(
        int workOrderId, string technicianId, byte[] png, byte[] rowVersion, CancellationToken ct);

    /// <summary>
    /// The stored signature image. <paramref name="requireOwnTechnician"/> hides another technician's job as
    /// not-found, exactly like reading the job itself.
    /// </summary>
    Task<ServiceResult<StoredFile>> OpenAsync(
        int workOrderId, string actingUserId, bool requireOwnTechnician, CancellationToken ct);
}
