using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.ServiceRequests;

/// <summary>
/// The service-request lifecycle (SDD §8) as a pure rule table: no database, no clock, so every
/// transition can be unit-tested directly. Phase 4 added Approved-&gt;Assigned (done directly in
/// WorkOrderService's own transaction, so it never appears as a row here); Phase 5 adds cancellation.
/// Cancel's extra actor-role rule (a Manager may cancel New or Approved; the creator-side roles only
/// New) is layered on top in ServiceRequestService — this table only says which targets are ever
/// structurally reachable from a given state.
/// </summary>
public static class ServiceRequestStateMachine
{
    private static readonly IReadOnlySet<(ServiceRequestStatus From, ServiceRequestStatus To)> Allowed =
        new HashSet<(ServiceRequestStatus, ServiceRequestStatus)>
        {
            (ServiceRequestStatus.New, ServiceRequestStatus.Approved),
            (ServiceRequestStatus.New, ServiceRequestStatus.Rejected),
            (ServiceRequestStatus.New, ServiceRequestStatus.Cancelled),
            (ServiceRequestStatus.Approved, ServiceRequestStatus.Cancelled)
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

        return to switch
        {
            ServiceRequestStatus.Approved =>
                $"This request is {from} and cannot be approved. Only a New request can be approved.",
            ServiceRequestStatus.Rejected =>
                $"This request is {from} and cannot be rejected. Only a New request can be rejected.",
            ServiceRequestStatus.Cancelled =>
                $"This request is {from} and cannot be cancelled. Only a New or Approved request can be cancelled.",
            _ => $"This request is {from} and cannot be moved to {to}."
        };
    }
}
