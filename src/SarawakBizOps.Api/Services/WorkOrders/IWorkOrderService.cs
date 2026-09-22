using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.WorkOrders;

public interface IWorkOrderService
{
    Task<ServiceResult<IReadOnlyList<WorkOrderDto>>> GetAllAsync(WorkOrderFilter filter, CancellationToken ct);
    Task<IReadOnlyList<WorkOrderDto>> GetMineAsync(string technicianId, CancellationToken ct);

    /// <summary>
    /// <paramref name="requireOwnTechnician"/> is true for a caller who only ever has the Technician
    /// role (BR-02): the record is reported not-found rather than 403 when it belongs to someone else,
    /// so a technician cannot even confirm another technician's job exists.
    /// </summary>
    Task<ServiceResult<WorkOrderDto>> GetByIdAsync(
        int id, string actingUserId, bool requireOwnTechnician, CancellationToken ct);

    /// <summary>Atomic: BR-01 (assign), BR-11 (active Technician), BR-12 (one WorkOrder per request), BR-13.</summary>
    Task<ServiceResult<WorkOrderDto>> AssignAsync(
        int serviceRequestId, string technicianId, string actingUserId, CancellationToken ct);

    Task<ServiceResult<WorkOrderDto>> StartAsync(int id, string technicianId, CancellationToken ct);

    Task<ServiceResult<WorkOrderDto>> UpdateDiagnosisAsync(
        int id, string technicianId, string diagnosis, string workPerformed, string? notes, CancellationToken ct);

    /// <summary>BR-03 (partial): requires a stored diagnosis and work-performed record (AC-05).</summary>
    Task<ServiceResult<WorkOrderDto>> CompleteAsync(int id, string technicianId, CancellationToken ct);

    /// <summary>Locks the work order (BR-06, AC-07 first cut; Phase 5 adds the central guard).</summary>
    Task<ServiceResult<WorkOrderDto>> ApproveAsync(int id, string actingUserId, CancellationToken ct);

    Task<IReadOnlyList<TechnicianSummaryDto>> GetTechniciansAsync(CancellationToken ct);
}
