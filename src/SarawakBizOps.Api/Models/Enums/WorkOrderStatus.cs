namespace SarawakBizOps.Api.Models.Enums;

// Section 9 (Work Order Lifecycle) of the design doc. A job is created by assigning a technician, so it starts
// Assigned; the design's "New" state was never produced and was removed (stored as text, so no data changed).
public enum WorkOrderStatus
{
    Assigned,
    InProgress,
    PendingParts,
    Completed,
    Approved,
    Cancelled
}
