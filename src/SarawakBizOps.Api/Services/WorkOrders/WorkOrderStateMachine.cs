using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// The work-order lifecycle (SDD §9) as a pure rule table, mirroring <c>ServiceRequestStateMachine</c>:
/// no database, no clock, so every transition can be unit-tested directly. Phase 4 covered the thin
/// happy path (Assigned-&gt;InProgress-&gt;Completed-&gt;Approved); Phase 5 adds cancellation from any
/// still-open state; Phase 7 adds InProgress &lt;-&gt; PendingParts.
/// </summary>
public static class WorkOrderStateMachine
{
    private static readonly IReadOnlySet<(WorkOrderStatus From, WorkOrderStatus To)> Allowed =
        new HashSet<(WorkOrderStatus, WorkOrderStatus)>
        {
            (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress),
            (WorkOrderStatus.InProgress, WorkOrderStatus.PendingParts),
            (WorkOrderStatus.PendingParts, WorkOrderStatus.InProgress),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Completed),
            (WorkOrderStatus.Completed, WorkOrderStatus.Approved),
            (WorkOrderStatus.New, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.Assigned, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled),
            (WorkOrderStatus.PendingParts, WorkOrderStatus.Cancelled)
        };

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to)
        => Allowed.Contains((from, to));

    /// <summary>True once a job is in a terminal state: the central lock guard checked before any
    /// mutating call — not just a status transition — so a locked job can never be edited again.</summary>
    public static bool IsLocked(WorkOrderStatus status)
        => status is WorkOrderStatus.Approved or WorkOrderStatus.Cancelled;

    public static string LockedMessage(WorkOrderStatus status)
        => $"This job is {status} and is locked against further changes.";

    /// <summary>Parts are added to or returned from a job only while it is being worked on.</summary>
    public static bool AcceptsPartChanges(WorkOrderStatus status) => status == WorkOrderStatus.InProgress;

    /// <summary>Photos are taken or removed only while the job is being worked on (Phase 9): once it is
    /// completed they are part of the record the Manager approves, so they can no longer be removed.</summary>
    public static bool AcceptsPhotoChanges(WorkOrderStatus status) => status == WorkOrderStatus.InProgress;

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
            WorkOrderStatus.PendingParts => ("paused for parts", "Only a job that is in progress can wait for parts."),
            WorkOrderStatus.Completed => ("completed", "Only a job that is in progress can be completed."),
            WorkOrderStatus.Approved => ("approved", "Only a completed job can be approved."),
            WorkOrderStatus.Cancelled => ("cancelled", "Only a New, Assigned, InProgress or PendingParts job can be cancelled."),
            _ => ($"moved to {to}", string.Empty)
        };

        return requirement.Length == 0
            ? $"This job is {from} and cannot be {action}."
            : $"This job is {from} and cannot be {action}. {requirement}";
    }

    /// <summary>
    /// Start and resume both lead to InProgress, so the table alone would let "start" resume a paused job
    /// (skipping the stock guard) or "resume" restart an Assigned one. Each action therefore also names
    /// the one state it leaves from.
    /// </summary>
    public static string? StartRefusal(WorkOrderStatus from)
        => from == WorkOrderStatus.Assigned
            ? null
            : $"This job is {from} and cannot be started. Only an Assigned job can be started.";

    /// <inheritdoc cref="StartRefusal"/>
    public static string? ResumeRefusal(WorkOrderStatus from)
        => from == WorkOrderStatus.PendingParts
            ? null
            : $"This job is {from} and cannot be resumed. Only a job waiting for parts can be resumed.";
}
