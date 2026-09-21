using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.ServiceRequests;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Pure unit tests (no database): every (from, to) pair of the request lifecycle. The expected set is
/// written out here independently of the implementation, so widening the rules on purpose means
/// changing this table too. Phase 4 adds Approved->Assigned and Phase 5 the cancellations.
/// </summary>
public class ServiceRequestStateMachineTests
{
    private static readonly (ServiceRequestStatus From, ServiceRequestStatus To)[] Valid =
    {
        (ServiceRequestStatus.New, ServiceRequestStatus.Approved),
        (ServiceRequestStatus.New, ServiceRequestStatus.Rejected)
    };

    public static TheoryData<ServiceRequestStatus, ServiceRequestStatus, bool> EveryPair()
    {
        var data = new TheoryData<ServiceRequestStatus, ServiceRequestStatus, bool>();
        foreach (var from in Enum.GetValues<ServiceRequestStatus>())
        {
            foreach (var to in Enum.GetValues<ServiceRequestStatus>())
            {
                data.Add(from, to, Valid.Contains((from, to)));
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_the_defined_transitions_are_allowed(ServiceRequestStatus from, ServiceRequestStatus to, bool expected)
    {
        Assert.Equal(expected, ServiceRequestStateMachine.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Refusal_message_exists_exactly_when_the_transition_is_refused(
        ServiceRequestStatus from, ServiceRequestStatus to, bool allowed)
    {
        var refusal = ServiceRequestStateMachine.Refusal(from, to);

        Assert.Equal(allowed, refusal is null);
    }

    [Theory]
    [InlineData(ServiceRequestStatus.Approved, ServiceRequestStatus.Approved, "Approved")]
    [InlineData(ServiceRequestStatus.Rejected, ServiceRequestStatus.Approved, "Rejected")]
    [InlineData(ServiceRequestStatus.Assigned, ServiceRequestStatus.Rejected, "Assigned")]
    [InlineData(ServiceRequestStatus.Cancelled, ServiceRequestStatus.Approved, "Cancelled")]
    public void Refusal_names_the_current_status_so_the_user_knows_why(
        ServiceRequestStatus from, ServiceRequestStatus to, string expectedText)
    {
        var refusal = ServiceRequestStateMachine.Refusal(from, to);

        Assert.NotNull(refusal);
        Assert.Contains(expectedText, refusal);
        Assert.Contains("Only a New request", refusal);
    }
}
