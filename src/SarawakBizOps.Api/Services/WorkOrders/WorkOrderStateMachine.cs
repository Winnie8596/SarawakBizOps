using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// The work-order lifecycle (SDD §9) as a pure rule table, mirroring <c>ServiceRequestStateMachine</c>:
/// no database, no clock, so every transition can be unit-tested directly. Phase 4 covered the thin
/// happy path (Assigned-&gt;InProgress-&gt;Completed-&gt;Approved); Phase 5 adds cancellation from any
/// still-open state. PendingParts (Phase 7) arrives later, by adding rows here.
/// </summary>
public static class WorkOrderStateMachine
{
    private static readonly IReadOnlySet<(WorkOrderStatus From, WorkOrderStatus To)> Allowed =
        new HashSet<(WorkOrderStatus, WorkOrderStatus)>
        {
            (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Completed),
            (WorkOrderStatus.Completed, WorkOrderStatus.Approved),
            (WorkOrderStatus.New, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.Assigned, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled)
        };

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to)
        => Allowed.Contains((from, to));

    /// <summary>True once a job is in a terminal state: the central lock guard checked before any
    /// mutating call — not just a status transition — so a locked job can never be edited again.</summary>
    public static bool IsLocked(WorkOrderStatus status)
        => status is WorkOrderStatus.Approved or WorkOrderStatus.Cancelled;

    public static string LockedMessage(WorkOrderStatus status)
        => $"This job is {status} and is locked against further changes.";

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
            WorkOrderStatus.Cancelled => ("cancelled", "Only a New, Assigned or InProgress job can be cancelled."),
            _ => ($"moved to {to}", string.Empty)
        };

        return requirement.Length == 0
            ? $"This job is {from} and cannot be {action}."
            : $"This job is {from} and cannot be {action}. {requirement}";
    }
}
