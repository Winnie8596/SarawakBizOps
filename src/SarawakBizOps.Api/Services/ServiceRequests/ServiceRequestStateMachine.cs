using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.ServiceRequests;

/// <summary>
/// The service-request lifecycle (SDD §8) as a pure rule table: no database, no clock, so every
/// transition can be unit-tested directly. Phase 3 knows New→Approved and New→Rejected; assignment
/// (Approved→Assigned) arrives in Phase 4 and cancellation in Phase 5, each by adding a row here.
/// </summary>
public static class ServiceRequestStateMachine
{
    private static readonly IReadOnlySet<(ServiceRequestStatus From, ServiceRequestStatus To)> Allowed =
        new HashSet<(ServiceRequestStatus, ServiceRequestStatus)>
        {
            (ServiceRequestStatus.New, ServiceRequestStatus.Approved),
            (ServiceRequestStatus.New, ServiceRequestStatus.Rejected)
        };

    public static bool CanTransition(ServiceRequestStatus from, ServiceRequestStatus to)
        => Allowed.Contains((from, to));

    /// <summary>A human-readable reason why the transition is refused, or null when it is allowed.</summary>
    public static string? Refusal(ServiceRequestStatus from, ServiceRequestStatus to)
    {
        if (CanTransition(from, to))
        {
            return null;
        }

        var action = to switch
        {
            ServiceRequestStatus.Approved => "approved",
            ServiceRequestStatus.Rejected => "rejected",
            _ => $"moved to {to}"
        };

        return $"This request is {from} and cannot be {action}. Only a New request can be approved or rejected.";
    }
}
