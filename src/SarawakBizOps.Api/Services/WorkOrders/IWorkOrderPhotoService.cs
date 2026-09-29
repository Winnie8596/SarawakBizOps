using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>Phase 9: photos taken on a job (PRD §6.6), stored through <see cref="IFileStorage"/>.</summary>
public interface IWorkOrderPhotoService
{
    /// <summary>
    /// Adds one photo to the job. Assigned technician only (BR-02), only while the job is InProgress, and at
    /// most <see cref="PhotoRules.MaxPhotosPerJob"/>; carries and bumps the job's rowVersion like every other
    /// mutating call.
    /// </summary>
    Task<ServiceResult<WorkOrderDto>> AddAsync(
        int workOrderId, string technicianId, byte[] image, byte[] rowVersion, CancellationToken ct);

    /// <summary>Removes a photo — only while the job is still InProgress, so a completed job's evidence stays.</summary>
    Task<ServiceResult<WorkOrderDto>> RemoveAsync(
        int workOrderId, int photoId, string technicianId, byte[] rowVersion, CancellationToken ct);

    /// <summary>
    /// The stored image. <paramref name="requireOwnTechnician"/> hides another technician's job as not-found,
    /// exactly like reading the job itself.
    /// </summary>
    Task<ServiceResult<StoredFile>> OpenAsync(
        int workOrderId, int photoId, string actingUserId, bool requireOwnTechnician, CancellationToken ct);
}
