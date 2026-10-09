using SarawakBizOps.Api.DTOs.Dashboard;

namespace SarawakBizOps.Api.Services.Analytics;

/// <summary>
/// Phase 11 (PRD §6.9, SDD §14): the dashboard's numbers as <b>named, parameterised operations</b>, each one
/// query, each independently testable. Phase 14's Manager AI calls these same operations (AC-10: it answers
/// from backend-computed data only), so each keeps a precise, documented definition — see <see cref="AnalyticsRules"/>.
/// "Now" comes from the injected <see cref="TimeProvider"/>. Phase 12 adds the rest of PRD §6.9 and the per-role
/// dashboards (PRD §5).
/// </summary>
public interface IAnalyticsService
{
    /// <summary>Open requests (New or Approved) per priority, most urgent first; every priority present.</summary>
    Task<IReadOnlyList<CountDto>> OpenRequestsByPriorityAsync(CancellationToken ct);

    /// <summary>All work orders per status, Cancelled included, in lifecycle order; every status present.</summary>
    Task<IReadOnlyList<CountDto>> WorkOrdersByStatusAsync(CancellationToken ct);

    /// <summary>
    /// Jobs completed (Completed or Approved) per Malaysia-time calendar month, by <c>CompletedAt</c>, for the
    /// <paramref name="months"/> months ending with the current one; oldest first, zero-filled.
    /// </summary>
    Task<IReadOnlyList<MonthCountDto>> JobsCompletedPerMonthAsync(int months, CancellationToken ct);

    /// <summary>How many active parts are low (and out) of stock, and the <paramref name="top"/> most urgent.</summary>
    Task<LowStockSummaryDto> LowStockSummaryAsync(int top, CancellationToken ct);

    // ---- Phase 12: analytics depth. Each takes the period as a number of MYT months ending with the current
    // one — the same window as JobsCompletedPerMonth, so the dashboard's numbers all describe one period. ----

    /// <summary>
    /// Mean time from a request being raised (<c>CreatedAt</c>) to its job being assigned (<c>AssignedAt</c>), over
    /// every job assigned in the period — a job later cancelled included (the assignment happened), and a request
    /// reassigned after a cancel measured again from when it was raised.
    /// </summary>
    Task<DurationStatDto> AverageTimeToAssignmentAsync(int months, CancellationToken ct);

    /// <summary>
    /// Mean time from a job being started (<c>StartedAt</c>) to being completed (<c>CompletedAt</c>), over jobs
    /// completed (Completed or Approved) in the period. Waiting for parts is part of the duration.
    /// </summary>
    Task<DurationStatDto> AverageCompletionDurationAsync(int months, CancellationToken ct);

    /// <summary>
    /// Equipment with at least <paramref name="minimumRequests"/> fault reports (requests not Cancelled or
    /// Rejected) raised in the period: how many there are, and the <paramref name="top"/> with the most reports.
    /// </summary>
    Task<RepeatedFailuresDto> RepeatedFailuresByEquipmentAsync(int months, int minimumRequests, int top, CancellationToken ct);

    /// <summary>
    /// The <paramref name="top"/> parts by cost used on jobs in the period (by when each line was added), with
    /// quantity and job count. Each line's cost is rounded to sen before summing, as a job's total is. Net of
    /// returns — a returned line no longer exists — and a cancelled job's parts included, since that stock left
    /// the store.
    /// </summary>
    Task<IReadOnlyList<PartUsageDto>> TopPartsByUsageAsync(int months, int top, CancellationToken ct);

    /// <summary>Jobs paused as PendingParts, the longest-waiting first, and whether their part is back in stock.</summary>
    Task<JobsWaitingForPartsDto> JobsWaitingForPartsAsync(int top, CancellationToken ct);

    /// <summary>
    /// The service desk's queue: requests awaiting review (New) and assignment (Approved), the <paramref name="top"/>
    /// longest-waiting of them, and requests rejected in the last <paramref name="recentDays"/> days.
    /// </summary>
    Task<RequestQueueDto> RequestQueueAsync(int top, int recentDays, CancellationToken ct);

    /// <summary>The whole Manager dashboard: every operation above plus the headline tiles derived from them, over
    /// the <paramref name="months"/> MYT months ending with the current one.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(int months, CancellationToken ct);

    /// <summary>The WarehouseStaff dashboard (Phase 12): stock, jobs waiting for parts, parts usage over the
    /// <paramref name="months"/> MYT months ending with the current one.</summary>
    Task<WarehouseDashboardDto> GetWarehouseDashboardAsync(int months, CancellationToken ct);

    /// <summary>The ServiceStaff dashboard (Phase 12): the request queue.</summary>
    Task<ServiceQueueDashboardDto> GetServiceQueueDashboardAsync(CancellationToken ct);
}
