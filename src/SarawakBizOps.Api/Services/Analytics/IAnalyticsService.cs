using SarawakBizOps.Api.DTOs.Dashboard;

namespace SarawakBizOps.Api.Services.Analytics;

/// <summary>
/// Phase 11 (PRD §6.9, SDD §14): the dashboard's numbers as <b>named, parameterised operations</b>, each one
/// query, each independently testable. Phase 14's Manager AI will call these same operations (AC-10: it answers
/// from backend-computed data only), so each keeps a precise, documented definition — see <see cref="AnalyticsRules"/>.
/// "Now" comes from the injected <see cref="TimeProvider"/>.
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

    /// <summary>The whole Manager dashboard: every operation above plus the headline tiles derived from them.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
}
