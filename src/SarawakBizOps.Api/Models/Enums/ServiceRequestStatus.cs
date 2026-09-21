namespace SarawakBizOps.Api.Models.Enums;

// Section 6.3 / Section 8 (Service Request Lifecycle) of the design doc.
// New -> Approved -> Assigned, with Rejected/Cancelled as alternative paths.
public enum ServiceRequestStatus
{
    New,
    Approved,
    Assigned,
    Rejected,
    Cancelled
}
