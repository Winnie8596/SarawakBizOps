using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Services.Analytics;

/// <summary>
/// The dashboard's shaping rules (Phase 11, PRD §6.9, SDD §14) as a pure class, unit-testable without a database:
/// which Malaysia-time months a trend covers, how the grouped counts the database returns become complete,
/// zero-filled series in a fixed order, and how the headline tiles are derived from them. Phase 12 adds the
/// definitions of the turnaround, repeated-failure and parts-usage metrics and which dashboard each role gets.
/// </summary>
public static class AnalyticsRules
{
    /// <summary>The completed-jobs trend covers this many months, the current MYT month included.</summary>
    public const int DefaultMonths = 6;

    public const int MaxMonths = 24;

    /// <summary>How many of the most urgent low-stock parts the summary lists by name.</summary>
    public const int LowStockItems = 5;

    /// <summary>Phase 12: a piece of equipment has repeated failures with at least this many fault reports in the
    /// period (six months by default): a fault once a quarter or more often is worth a Manager's look.</summary>
    public const int DefaultRepeatedFailureThreshold = 3;

    /// <summary>How many rows each Phase 12 list shows on the Manager's dashboard.</summary>
    public const int ManagerListItems = 5;

    /// <summary>The warehouse view lists more of the low-stock parts and waiting jobs: they are its work queue.</summary>
    public const int WarehouseListItems = 10;

    /// <summary>The service queue lists this many oldest open and recently rejected requests.</summary>
    public const int QueueListItems = 5;

    /// <summary>"Recently rejected" on the service queue: within this many days.</summary>
    public const int RecentRejectionDays = 30;

    /// <summary>A service request is a fault report unless it was withdrawn (Cancelled — raised by mistake or
    /// resolved before review) or turned down (Rejected — e.g. equipment not worth repairing). New, Approved and
    /// Assigned requests all count, whatever became of their job.</summary>
    public static readonly ServiceRequestStatus[] FaultReportStatuses =
        { ServiceRequestStatus.New, ServiceRequestStatus.Approved, ServiceRequestStatus.Assigned };

    /// <summary>A request is open while someone still has to act on it before a job exists: awaiting review
    /// (New) or approved and awaiting assignment (Approved — including one whose job was cancelled, which reverts
    /// it to Approved). Assigned requests are followed through their work order; Rejected and Cancelled are closed.</summary>
    public static readonly ServiceRequestStatus[] OpenRequestStatuses =
        { ServiceRequestStatus.New, ServiceRequestStatus.Approved };

    /// <summary>A job counts as completed once the technician has completed it, whether or not the Manager has
    /// approved it yet. A job can only be cancelled before completion, so no cancelled job is ever counted.</summary>
    public static readonly WorkOrderStatus[] CompletedJobStatuses =
        { WorkOrderStatus.Completed, WorkOrderStatus.Approved };

    /// <summary>A job is active while it is with a technician: assigned, being worked on or waiting for parts.</summary>
    public static readonly WorkOrderStatus[] ActiveJobStatuses =
        { WorkOrderStatus.Assigned, WorkOrderStatus.InProgress, WorkOrderStatus.PendingParts };

    /// <summary>Priorities as the Manager reads them: most urgent first.</summary>
    public static readonly RequestPriority[] PriorityOrder =
        { RequestPriority.Urgent, RequestPriority.High, RequestPriority.Medium, RequestPriority.Low };

    /// <summary>Statuses in lifecycle order (SDD §9), the terminal Cancelled last.</summary>
    public static readonly WorkOrderStatus[] StatusOrder =
    {
        WorkOrderStatus.Assigned, WorkOrderStatus.InProgress, WorkOrderStatus.PendingParts,
        WorkOrderStatus.Completed, WorkOrderStatus.Approved, WorkOrderStatus.Cancelled
    };

    /// <summary>
    /// The <paramref name="months"/> calendar months in Malaysia time (UTC+8, no DST — PRD Q-02) ending with the
    /// month that contains <paramref name="nowUtc"/>, oldest first, and the UTC instants that bound them:
    /// <c>FromUtc</c> is 00:00 MYT on the first day of the oldest month (inclusive), <c>ToUtc</c> is 00:00 MYT on the
    /// first day of the month after the current one (exclusive). A job completed at 23:30 MYT on 31 August
    /// (15:30 UTC) is therefore an August job, and one completed at 00:30 MYT on 1 September (16:30 UTC on 31
    /// August) a September job.
    /// </summary>
    public static MonthWindow MonthsEnding(DateTime nowUtc, int months)
    {
        if (months is < 1 or > MaxMonths)
        {
            throw new ArgumentOutOfRangeException(nameof(months), months, $"Between 1 and {MaxMonths} months.");
        }

        var nowMyt = ReportRules.ToMyt(nowUtc);
        var current = new DateOnly(nowMyt.Year, nowMyt.Month, 1);
        var first = current.AddMonths(-(months - 1));
        var list = Enumerable.Range(0, months).Select(i => first.AddMonths(i)).ToList();

        return new MonthWindow(
            list.Select(m => (m.Year, m.Month)).ToList(),
            MytDayStartUtc(first),
            MytDayStartUtc(current.AddMonths(1)));
    }

    /// <summary>
    /// 00:00 MYT on <paramref name="day"/> as a UTC instant (16:00 UTC the day before). The one place a Malaysian
    /// calendar day becomes a UTC bound: the month windows above and the inventory history's day filters both use it,
    /// so no client has to know the offset.
    /// </summary>
    public static DateTime MytDayStartUtc(DateOnly day)
        => DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) - ReportRules.MytOffset, DateTimeKind.Utc);

    /// <summary>The period a window covers, as the payload shows it.</summary>
    public static AnalyticsPeriodDto Period(MonthWindow window) => new()
    {
        FromMonth = MonthKey(window.Months[0].Year, window.Months[0].Month),
        ToMonth = MonthKey(window.Months[^1].Year, window.Months[^1].Month),
        FromUtc = window.FromUtc,
        ToUtc = window.ToUtc
    };

    /// <summary>An average the database computed in seconds, as hours rounded to two decimals; null when there was
    /// nothing to average.</summary>
    public static DurationStatDto Duration(double? averageSeconds, int count) => new()
    {
        AverageHours = count == 0 || averageSeconds is null ? null : Math.Round(averageSeconds.Value / 3600, 2),
        Count = count
    };

    /// <summary>
    /// Which dashboard a caller gets (PRD §5): Admin and Manager the full one; WarehouseStaff the stock view;
    /// ServiceStaff the request queue. Someone holding several roles gets the broadest of them, in that order. A
    /// Technician (only) gets none — their home is My Jobs — so null.
    /// </summary>
    public static string? ViewFor(Func<string, bool> isInRole)
    {
        if (isInRole(AppRoles.Admin) || isInRole(AppRoles.Manager)) return DashboardViews.Manager;
        if (isInRole(AppRoles.WarehouseStaff)) return DashboardViews.Warehouse;
        if (isInRole(AppRoles.ServiceStaff)) return DashboardViews.ServiceQueue;
        return null;
    }

    /// <summary>"2026-09": sortable, and trivially formatted by the UI.</summary>
    public static string MonthKey(int year, int month) => $"{year:D4}-{month:D2}";

    /// <summary>One entry per month of the window, oldest first, zero where nothing was completed.</summary>
    public static IReadOnlyList<MonthCountDto> FillMonths(
        MonthWindow window, IReadOnlyDictionary<(int Year, int Month), int> counts)
        => window.Months
            .Select(m => new MonthCountDto { Month = MonthKey(m.Year, m.Month), Count = counts.GetValueOrDefault(m) })
            .ToList();

    /// <summary>Every priority, most urgent first, zero where none are open.</summary>
    public static IReadOnlyList<CountDto> FillPriorities(IReadOnlyDictionary<RequestPriority, int> counts)
        => PriorityOrder.Select(p => new CountDto { Key = p.ToString(), Count = counts.GetValueOrDefault(p) }).ToList();

    /// <summary>Every status, in lifecycle order, zero where there are none.</summary>
    public static IReadOnlyList<CountDto> FillStatuses(IReadOnlyDictionary<WorkOrderStatus, int> counts)
        => StatusOrder.Select(s => new CountDto { Key = s.ToString(), Count = counts.GetValueOrDefault(s) }).ToList();

    /// <summary>
    /// The headline tiles, derived only from the series above so a tile can never disagree with its chart:
    /// open requests (sum of the priority series), active jobs (<see cref="ActiveJobStatuses"/>), jobs awaiting
    /// the Manager's approval (Completed), jobs completed in the current MYT month (the last month of the trend)
    /// and low-stock parts.
    /// </summary>
    public static DashboardKpisDto Kpis(
        IReadOnlyList<CountDto> openRequestsByPriority,
        IReadOnlyList<CountDto> workOrdersByStatus,
        IReadOnlyList<MonthCountDto> jobsCompletedPerMonth,
        LowStockSummaryDto lowStock)
    {
        int Status(WorkOrderStatus s) => workOrdersByStatus.FirstOrDefault(c => c.Key == s.ToString())?.Count ?? 0;

        return new DashboardKpisDto
        {
            OpenRequests = openRequestsByPriority.Sum(c => c.Count),
            ActiveWorkOrders = ActiveJobStatuses.Sum(Status),
            AwaitingApproval = Status(WorkOrderStatus.Completed),
            CompletedThisMonth = jobsCompletedPerMonth.Count == 0 ? 0 : jobsCompletedPerMonth[^1].Count,
            LowStockParts = lowStock.Count
        };
    }
}

/// <summary>Calendar months (MYT), oldest first, and the UTC range [FromUtc, ToUtc) that covers exactly them.</summary>
public record MonthWindow(IReadOnlyList<(int Year, int Month)> Months, DateTime FromUtc, DateTime ToUtc);
