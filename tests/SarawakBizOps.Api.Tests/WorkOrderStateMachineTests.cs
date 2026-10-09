using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Pure unit tests (no database): every (from, to) pair of the work-order lifecycle. The expected set
/// is written out here independently of the implementation, so widening the rules on purpose means
/// changing this table too. Phase 5 adds Cancelled; Phase 7 adds PendingParts and resume.
/// </summary>
public class WorkOrderStateMachineTests
{
    private static readonly (WorkOrderStatus From, WorkOrderStatus To)[] Valid =
    {
        (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress),
        (WorkOrderStatus.InProgress, WorkOrderStatus.PendingParts),
        (WorkOrderStatus.PendingParts, WorkOrderStatus.InProgress),
        (WorkOrderStatus.InProgress, WorkOrderStatus.Completed),
        (WorkOrderStatus.Completed, WorkOrderStatus.Approved),
        (WorkOrderStatus.Assigned, WorkOrderStatus.Cancelled),
        (WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled),
        (WorkOrderStatus.PendingParts, WorkOrderStatus.Cancelled)
    };

    public static TheoryData<WorkOrderStatus, WorkOrderStatus, bool> EveryPair()
    {
        var data = new TheoryData<WorkOrderStatus, WorkOrderStatus, bool>();
        foreach (var from in Enum.GetValues<WorkOrderStatus>())
        {
            foreach (var to in Enum.GetValues<WorkOrderStatus>())
            {
                data.Add(from, to, Valid.Contains((from, to)));
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_the_defined_transitions_are_allowed(WorkOrderStatus from, WorkOrderStatus to, bool expected)
    {
        Assert.Equal(expected, WorkOrderStateMachine.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Refusal_message_exists_exactly_when_the_transition_is_refused(
        WorkOrderStatus from, WorkOrderStatus to, bool allowed)
    {
        var refusal = WorkOrderStateMachine.Refusal(from, to);

        Assert.Equal(allowed, refusal is null);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Assigned, WorkOrderStatus.Completed, "Assigned")]
    [InlineData(WorkOrderStatus.Approved, WorkOrderStatus.InProgress, "Approved")]
    [InlineData(WorkOrderStatus.Cancelled, WorkOrderStatus.Approved, "Cancelled")]
    public void Refusal_names_the_current_status_so_the_user_knows_why(
        WorkOrderStatus from, WorkOrderStatus to, string expectedText)
    {
        var refusal = WorkOrderStateMachine.Refusal(from, to);

        Assert.NotNull(refusal);
        Assert.Contains(expectedText, refusal);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed, "Completed")]
    [InlineData(WorkOrderStatus.Approved, "Approved")]
    [InlineData(WorkOrderStatus.Cancelled, "Cancelled")]
    public void Cancel_refusal_names_the_current_status_and_the_cancellable_states(
        WorkOrderStatus from, string expectedText)
    {
        var refusal = WorkOrderStateMachine.Refusal(from, WorkOrderStatus.Cancelled);

        Assert.NotNull(refusal);
        Assert.Contains(expectedText, refusal);
        Assert.Contains("Only an Assigned, InProgress or PendingParts job", refusal);
    }

    // Start and resume both reach InProgress; each is allowed from exactly one state, so "start" cannot
    // skip the resume stock guard and "resume" cannot restart an Assigned job.
    public static TheoryData<WorkOrderStatus> EveryStatus()
    {
        var data = new TheoryData<WorkOrderStatus>();
        foreach (var status in Enum.GetValues<WorkOrderStatus>())
        {
            data.Add(status);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Start_is_allowed_only_from_Assigned(WorkOrderStatus from)
    {
        var refusal = WorkOrderStateMachine.StartRefusal(from);

        Assert.Equal(from == WorkOrderStatus.Assigned, refusal is null);
        if (refusal is not null) Assert.Contains(from.ToString(), refusal);
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Resume_is_allowed_only_from_PendingParts(WorkOrderStatus from)
    {
        var refusal = WorkOrderStateMachine.ResumeRefusal(from);

        Assert.Equal(from == WorkOrderStatus.PendingParts, refusal is null);
        if (refusal is not null) Assert.Contains(from.ToString(), refusal);
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Parts_change_only_while_a_job_is_in_progress(WorkOrderStatus status)
    {
        Assert.Equal(status == WorkOrderStatus.InProgress, WorkOrderStateMachine.AcceptsPartChanges(status));
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Photos_are_added_or_removed_only_while_a_job_is_in_progress(WorkOrderStatus status)
    {
        Assert.Equal(status == WorkOrderStatus.InProgress, WorkOrderStateMachine.AcceptsPhotoChanges(status));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStatus.Cancelled)]
    public void Locked_states_are_exactly_Approved_and_Cancelled(WorkOrderStatus status)
    {
        Assert.True(WorkOrderStateMachine.IsLocked(status));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Assigned)]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.PendingParts)]
    [InlineData(WorkOrderStatus.Completed)]
    public void Open_states_are_not_locked(WorkOrderStatus status)
    {
        Assert.False(WorkOrderStateMachine.IsLocked(status));
    }
}
