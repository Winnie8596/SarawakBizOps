using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// The work-order lifecycle (SDD §9) as a pure rule table, mirroring <c>ServiceRequestStateMachine</c>:
/// no database, no clock, so every transition can be unit-tested directly. Phase 4 knows the thin
/// happy path only — Assigned→InProgress→Completed→Approved. PendingParts (Phase 7) and Cancelled
/// (Phase 5) arrive later, each by adding rows here.
/// </summary>
public static class WorkOrderStateMachine
{
    private static readonly IReadOnlySet<(WorkOrderStatus From, WorkOrderStatus To)> Allowed =
        new HashSet<(WorkOrderStatus, WorkOrderStatus)>
        {
            (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Completed),
            (WorkOrderStatus.Completed, WorkOrderStatus.Approved)
        };

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to)
        => Allowed.Contains((from, to));

    /// <summary>A human-readable reason why the transition is refused, or null when it is allowed.</summary>
    public static string? Refusal(WorkOrderStatus from, WorkOrderStatus to)
    {
        if (CanTransition(from, to))
        {
            return null;
        }

        var (action, requirement) = to switch
        {
            WorkOrderStatus.InProgress => ("started", "Only an Assigned job can be started."),
            WorkOrderStatus.Completed => ("completed", "Only a job that is in progress can be completed."),
            WorkOrderStatus.Approved => ("approved", "Only a completed job can be approved."),
            _ => ($"moved to {to}", string.Empty)
        };

        return requirement.Length == 0
            ? $"This job is {from} and cannot be {action}."
            : $"This job is {from} and cannot be {action}. {requirement}";
    }
}
