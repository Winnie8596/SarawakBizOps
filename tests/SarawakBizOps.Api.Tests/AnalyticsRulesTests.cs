using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 11: the dashboard's Malaysia-time month windows, zero-filling and headline tiles, without a database.
/// Phase 12: the period, duration rounding and which dashboard each role gets.</summary>
public class AnalyticsRulesTests
{
    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public void The_window_is_whole_MYT_months_ending_with_the_current_one()
    {
        var window = AnalyticsRules.MonthsEnding(Utc(2026, 9, 15, 4), 6);

        Assert.Equal(new[] { (2026, 4), (2026, 5), (2026, 6), (2026, 7), (2026, 8), (2026, 9) }, window.Months);
        Assert.Equal(Utc(2026, 3, 31, 16), window.FromUtc); // 00:00 MYT on 1 April
        Assert.Equal(Utc(2026, 9, 30, 16), window.ToUtc);   // 00:00 MYT on 1 October
        Assert.Equal(DateTimeKind.Utc, window.FromUtc.Kind);
    }

    [Fact]
    public void The_current_month_is_the_MYT_month_not_the_UTC_one()
    {
        // 16:30 UTC on 31 August is already 00:30 on 1 September in Kuching.
        var window = AnalyticsRules.MonthsEnding(Utc(2026, 8, 31, 16, 30), 1);

        Assert.Equal(new[] { (2026, 9) }, window.Months);
        Assert.Equal(Utc(2026, 8, 31, 16), window.FromUtc);

        // ...and 15:59:59 UTC is still 23:59:59 on 31 August.
        Assert.Equal(new[] { (2026, 8) }, AnalyticsRules.MonthsEnding(Utc(2026, 8, 31, 15, 59, 59), 1).Months);
    }

    [Fact]
    public void A_window_can_cross_a_year_boundary()
    {
        var window = AnalyticsRules.MonthsEnding(Utc(2027, 1, 5), 3);

        Assert.Equal(new[] { (2026, 11), (2026, 12), (2027, 1) }, window.Months);
        Assert.Equal(Utc(2026, 10, 31, 16), window.FromUtc);
        Assert.Equal(Utc(2027, 1, 31, 16), window.ToUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AnalyticsRules.MaxMonths + 1)]
    public void The_month_count_is_bounded(int months)
        => Assert.Throws<ArgumentOutOfRangeException>(() => AnalyticsRules.MonthsEnding(Utc(2026, 9, 1), months));

    [Fact]
    public void Months_without_completions_are_zero_and_the_series_is_oldest_first()
    {
        var window = AnalyticsRules.MonthsEnding(Utc(2026, 9, 15), 4);
        var counts = new Dictionary<(int, int), int> { [(2026, 7)] = 3, [(2026, 9)] = 1, [(2025, 1)] = 99 };

        var series = AnalyticsRules.FillMonths(window, counts);

        Assert.Equal(
            new[] { ("2026-06", 0), ("2026-07", 3), ("2026-08", 0), ("2026-09", 1) },
            series.Select(m => (m.Month, m.Count)));
    }

    [Fact]
    public void Every_priority_and_status_is_present_in_a_fixed_order()
    {
        var priorities = AnalyticsRules.FillPriorities(new Dictionary<RequestPriority, int> { [RequestPriority.Low] = 2 });
        var statuses = AnalyticsRules.FillStatuses(new Dictionary<WorkOrderStatus, int> { [WorkOrderStatus.Approved] = 4 });

        Assert.Equal(new[] { ("Urgent", 0), ("High", 0), ("Medium", 0), ("Low", 2) }, priorities.Select(c => (c.Key, c.Count)));
        Assert.Equal(
            new[] { "New", "Assigned", "InProgress", "PendingParts", "Completed", "Approved", "Cancelled" },
            statuses.Select(c => c.Key));
        Assert.Equal(4, statuses.Single(c => c.Key == "Approved").Count);
        Assert.Equal(4, statuses.Sum(c => c.Count));
    }

    [Fact]
    public void The_tiles_are_derived_from_the_series()
    {
        var kpis = AnalyticsRules.Kpis(
            AnalyticsRules.FillPriorities(new Dictionary<RequestPriority, int> { [RequestPriority.Urgent] = 1, [RequestPriority.Low] = 2 }),
            AnalyticsRules.FillStatuses(new Dictionary<WorkOrderStatus, int>
            {
                [WorkOrderStatus.Assigned] = 1, [WorkOrderStatus.InProgress] = 2, [WorkOrderStatus.PendingParts] = 3,
                [WorkOrderStatus.Completed] = 4, [WorkOrderStatus.Approved] = 5, [WorkOrderStatus.Cancelled] = 6
            }),
            new[] { new MonthCountDto { Month = "2026-08", Count = 7 }, new MonthCountDto { Month = "2026-09", Count = 2 } },
            new LowStockSummaryDto { Count = 3 });

        Assert.Equal(3, kpis.OpenRequests);
        Assert.Equal(6, kpis.ActiveWorkOrders); // Assigned + InProgress + PendingParts
        Assert.Equal(4, kpis.AwaitingApproval);
        Assert.Equal(2, kpis.CompletedThisMonth); // the last month of the trend
        Assert.Equal(3, kpis.LowStockParts);
    }

    [Fact]
    public void The_period_names_the_first_and_last_month_of_the_window()
    {
        var period = AnalyticsRules.Period(AnalyticsRules.MonthsEnding(Utc(2027, 1, 5), 6));

        Assert.Equal(("2026-08", "2027-01"), (period.FromMonth, period.ToMonth));
        Assert.Equal((Utc(2026, 7, 31, 16), Utc(2027, 1, 31, 16)), (period.FromUtc, period.ToUtc));
    }

    [Theory]
    [InlineData(171_874.3, 3, 47.74)] // seconds → hours, two decimals
    [InlineData(82_080.0, 10, 22.8)]
    [InlineData(0.0, 1, 0.0)]         // measured, and instant: zero, not "nothing"
    public void An_average_in_seconds_is_reported_in_hours(double seconds, int count, double hours)
    {
        var duration = AnalyticsRules.Duration(seconds, count);

        Assert.Equal(hours, duration.AverageHours);
        Assert.Equal(count, duration.Count);
    }

    [Fact]
    public void Nothing_measured_is_no_average_at_all()
    {
        Assert.Null(AnalyticsRules.Duration(null, 0).AverageHours);
        Assert.Equal(0, AnalyticsRules.Duration(null, 0).Count);
    }

    [Theory]
    [InlineData(new[] { AppRoles.Manager }, DashboardViews.Manager)]
    [InlineData(new[] { AppRoles.Admin }, DashboardViews.Manager)]
    [InlineData(new[] { AppRoles.WarehouseStaff }, DashboardViews.Warehouse)]
    [InlineData(new[] { AppRoles.ServiceStaff }, DashboardViews.ServiceQueue)]
    [InlineData(new[] { AppRoles.ServiceStaff, AppRoles.WarehouseStaff }, DashboardViews.Warehouse)] // the broader view
    [InlineData(new[] { AppRoles.WarehouseStaff, AppRoles.Manager }, DashboardViews.Manager)]
    [InlineData(new[] { AppRoles.Technician }, null)]
    [InlineData(new string[0], null)]
    public void Each_role_gets_its_own_dashboard(string[] roles, string? view)
    {
        Assert.Equal(view, AnalyticsRules.ViewFor(roles.Contains));
    }

    [Fact]
    public void Cancelled_and_rejected_requests_are_not_fault_reports()
    {
        Assert.Equal(
            new[] { ServiceRequestStatus.New, ServiceRequestStatus.Approved, ServiceRequestStatus.Assigned },
            AnalyticsRules.FaultReportStatuses);
    }
}
