using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// Parts used on a job (PRD §6.5 second half, SDD §10). Both operations are one database transaction each:
/// the WorkOrderPart line, exactly one ledger row linked to the job, and the QuantityOnHand change commit
/// together or not at all (BR-04, BR-05, BR-13 — AC-03/AC-04). Assigned technician only, InProgress only,
/// and both carry the work order's RowVersion like every other mutating call.
/// </summary>
public interface IWorkOrderPartService
{
    /// <summary>Issues stock to the job; refused (409, nothing written) when there is not enough on hand.</summary>
    Task<ServiceResult<WorkOrderDto>> AddAsync(
        int workOrderId, string technicianId, int partId, decimal quantity, byte[] rowVersion, CancellationToken ct);

    /// <summary>Removes a whole line and puts its quantity back into stock with one Return ledger row.</summary>
    Task<ServiceResult<WorkOrderDto>> ReturnAsync(
        int workOrderId, int lineId, string technicianId, byte[] rowVersion, CancellationToken ct);
}
