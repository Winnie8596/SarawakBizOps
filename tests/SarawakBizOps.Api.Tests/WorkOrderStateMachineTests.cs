using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Pure unit tests (no database): every (from, to) pair of the work-order lifecycle. The expected set
/// is written out here independently of the implementation, so widening the rules on purpose means
/// changing this table too. Phase 5 adds PendingParts/resume and Cancelled.
/// </summary>
public class WorkOrderStateMachineTests
{
    private static readonly (WorkOrderStatus From, WorkOrderStatus To)[] Valid =
    {
        (WorkOrderStatus.Assigned, WorkOrderStatus.InProgress),
        (WorkOrderStatus.InProgress, WorkOrderStatus.Completed),
        (WorkOrderStatus.Completed, WorkOrderStatus.Approved)
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
    [InlineData(WorkOrderStatus.New, WorkOrderStatus.InProgress, "New")]
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
}
