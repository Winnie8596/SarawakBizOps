using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Services.Parts;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Services.Analytics;

/// <summary>
/// Each operation is one grouped query — the database counts, only the handful of result rows come back — and
/// <see cref="AnalyticsRules"/> turns the rows into complete, ordered series. Reads only, no tracking.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public AnalyticsService(ApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CountDto>> OpenRequestsByPriorityAsync(CancellationToken ct)
    {
        // Served by IX_ServiceRequests_Status.
        var rows = await _db.ServiceRequests.AsNoTracking()
            .Where(r => AnalyticsRules.OpenRequestStatuses.Contains(r.Status))
            .GroupBy(r => r.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return AnalyticsRules.FillPriorities(rows.ToDictionary(r => r.Priority, r => r.Count));
    }

    public async Task<IReadOnlyList<CountDto>> WorkOrdersByStatusAsync(CancellationToken ct)
    {
        // Served by IX_WorkOrders_Status (an index-only count).
        var rows = await _db.WorkOrders.AsNoTracking()
            .GroupBy(w => w.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return AnalyticsRules.FillStatuses(rows.ToDictionary(r => r.Status, r => r.Count));
    }

    public async Task<IReadOnlyList<MonthCountDto>> JobsCompletedPerMonthAsync(int months, CancellationToken ct)
    {
        var window = AnalyticsRules.MonthsEnding(_clock.GetUtcNow().UtcDateTime, months);
        var offsetHours = ReportRules.MytOffset.Hours;

        // A range seek on IX_WorkOrders_CompletedAt (Status included), then bucketed by the MYT calendar month
        // in SQL: GROUP BY DATEPART(year/month, DATEADD(hour, 8, CompletedAt)).
        var rows = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.CompletedAt >= window.FromUtc && w.CompletedAt < window.ToUtc
                && AnalyticsRules.CompletedJobStatuses.Contains(w.Status))
            .Select(w => w.CompletedAt!.Value.AddHours(offsetHours))
            .GroupBy(myt => new { myt.Year, myt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(ct);

        return AnalyticsRules.FillMonths(window, rows.ToDictionary(r => (r.Year, r.Month), r => r.Count));
    }

    public async Task<LowStockSummaryDto> LowStockSummaryAsync(int top, CancellationToken ct)
    {
        // The parts catalogue is small (hundreds of rows) and the rule compares two columns of the same row, so a
        // scan is the plan either way; no index helps here.
        var low = _db.Parts.AsNoTracking().Where(StockLevels.IsLow);

        var counts = await low
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), OutOfStock = g.Count(p => p.QuantityOnHand <= 0) })
            .FirstOrDefaultAsync(ct);

        var items = await low
            .OrderBy(p => p.QuantityOnHand - p.MinimumStockLevel) // the same order as GET /api/parts/low-stock
            .ThenBy(p => p.PartNumber)
            .Take(Math.Max(top, 0))
            .Select(p => new LowStockItemDto
            {
                PartId = p.Id,
                PartNumber = p.PartNumber,
                Name = p.Name,
                Unit = p.Unit,
                QuantityOnHand = p.QuantityOnHand,
                MinimumStockLevel = p.MinimumStockLevel
            })
            .ToListAsync(ct);

        return new LowStockSummaryDto
        {
            Count = counts?.Count ?? 0,
            OutOfStock = counts?.OutOfStock ?? 0,
            Items = items
        };
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        // One DbContext cannot run queries concurrently, so the operations run one after another.
        var generatedAt = _clock.GetUtcNow().UtcDateTime;
        var byPriority = await OpenRequestsByPriorityAsync(ct);
        var byStatus = await WorkOrdersByStatusAsync(ct);
        var perMonth = await JobsCompletedPerMonthAsync(AnalyticsRules.DefaultMonths, ct);
        var lowStock = await LowStockSummaryAsync(AnalyticsRules.LowStockItems, ct);

        return new DashboardSummaryDto
        {
            GeneratedAt = generatedAt,
            Kpis = AnalyticsRules.Kpis(byPriority, byStatus, perMonth, lowStock),
            OpenRequestsByPriority = byPriority,
            WorkOrdersByStatus = byStatus,
            JobsCompletedPerMonth = perMonth,
            LowStock = lowStock
        };
    }
}
