using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Services.Analytics;

/// <summary>
/// The dashboard's shaping rules (Phase 11, PRD §6.9, SDD §14) as a pure class, unit-testable without a database:
/// which Malaysia-time months a trend covers, how the grouped counts the database returns become complete,
/// zero-filled series in a fixed order, and how the headline tiles are derived from them.
/// </summary>
public static class AnalyticsRules
{
    /// <summary>The completed-jobs trend covers this many months, the current MYT month included.</summary>
    public const int DefaultMonths = 6;

    public const int MaxMonths = 24;

    /// <summary>How many of the most urgent low-stock parts the summary lists by name.</summary>
    public const int LowStockItems = 5;

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
        WorkOrderStatus.New, WorkOrderStatus.Assigned, WorkOrderStatus.InProgress, WorkOrderStatus.PendingParts,
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
        var current = new DateTime(nowMyt.Year, nowMyt.Month, 1);
        var first = current.AddMonths(-(months - 1));
        var list = Enumerable.Range(0, months).Select(i => first.AddMonths(i)).ToList();

        return new MonthWindow(
            list.Select(m => (m.Year, m.Month)).ToList(),
            DateTime.SpecifyKind(first - ReportRules.MytOffset, DateTimeKind.Utc),
            DateTime.SpecifyKind(current.AddMonths(1) - ReportRules.MytOffset, DateTimeKind.Utc));
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
