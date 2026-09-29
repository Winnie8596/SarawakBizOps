using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 10: when a service report may be generated (BR-14 per D-05), its number and its storage key.</summary>
public class ReportRulesTests
{
    [Fact]
    public void A_completed_job_can_be_generated_and_regenerated()
    {
        Assert.Null(ReportRules.GenerateRefusal(WorkOrderStatus.Completed, null));
        Assert.Null(ReportRules.GenerateRefusal(WorkOrderStatus.Completed, "SR-2026-000001"));
    }

    [Fact]
    public void An_approved_job_gets_at_most_one_report_and_it_is_then_locked()
    {
        Assert.Null(ReportRules.GenerateRefusal(WorkOrderStatus.Approved, null));
        Assert.Equal("This job is Approved, so its service report SR-2026-000001 is locked and cannot be regenerated.",
            ReportRules.GenerateRefusal(WorkOrderStatus.Approved, "SR-2026-000001"));
    }

    [Theory]
    [InlineData(WorkOrderStatus.New)]
    [InlineData(WorkOrderStatus.Assigned)]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.PendingParts)]
    public void An_unfinished_job_has_no_report_yet(WorkOrderStatus status)
        => Assert.Equal($"This job is {status}; a service report can only be generated once the job is completed.",
            ReportRules.GenerateRefusal(status, null));

    [Fact]
    public void A_cancelled_job_has_no_report()
        => Assert.Equal("This job is Cancelled; a cancelled job has no service report.",
            ReportRules.GenerateRefusal(WorkOrderStatus.Cancelled, null));

    [Theory]
    [InlineData(2026, 123, "SR-2026-000123")]
    [InlineData(2027, 1, "SR-2027-000001")]
    [InlineData(2026, 1234567, "SR-2026-1234567")] // the sequence never wraps; the number just grows
    public void Report_numbers_carry_the_year_and_a_zero_padded_sequence(int year, long sequence, string expected)
        => Assert.Equal(expected, ReportRules.FormatNumber(year, sequence));

    [Fact]
    public void Every_rendering_gets_a_fresh_key_under_its_work_order()
    {
        var first = ReportRules.NewKey(41);
        var second = ReportRules.NewKey(41);

        Assert.NotEqual(first, second);
        Assert.Matches(@"^work-orders/41/report-[0-9a-f]{32}\.pdf$", first);
    }

    [Fact]
    public void Malaysia_time_is_utc_plus_eight_across_midnight_and_new_year()
    {
        var myt = ReportRules.ToMyt(new DateTime(2026, 12, 31, 16, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2027, 1, 1, 0, 30, 0), myt);
        Assert.Equal(DateTimeKind.Unspecified, myt.Kind);
    }
}
