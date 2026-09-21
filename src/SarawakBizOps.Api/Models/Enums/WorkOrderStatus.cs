namespace SarawakBizOps.Api.Models.Enums;

// Section 9 (Work Order Lifecycle) of the design doc.
public enum WorkOrderStatus
{
    New,
    Assigned,
    InProgress,
    PendingParts,
    Completed,
    Approved,
    Cancelled
}
