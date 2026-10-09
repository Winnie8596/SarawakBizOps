using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
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

    private MonthWindow Window(int months) => AnalyticsRules.MonthsEnding(_clock.GetUtcNow().UtcDateTime, months);

    public async Task<DurationStatDto> AverageTimeToAssignmentAsync(int months, CancellationToken ct)
    {
        var window = Window(months);

        // AVG(CAST(DATEDIFF(second, r.CreatedAt, w.AssignedAt) AS float)) in SQL: the float cast keeps SQL Server
        // from averaging integers into an integer. Only the one aggregate row comes back.
        var row = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.AssignedAt >= window.FromUtc && w.AssignedAt < window.ToUtc)
            .Select(w => (double)EF.Functions.DateDiffSecond(w.ServiceRequest!.CreatedAt, w.AssignedAt!.Value))
            .GroupBy(_ => 1)
            .Select(g => new { Average = g.Average(), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        return AnalyticsRules.Duration(row?.Average, row?.Count ?? 0);
    }

    public async Task<DurationStatDto> AverageCompletionDurationAsync(int months, CancellationToken ct)
    {
        var window = Window(months);

        // The same range seek on IX_WorkOrders_CompletedAt as the monthly trend.
        var row = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.CompletedAt >= window.FromUtc && w.CompletedAt < window.ToUtc && w.StartedAt != null
                && AnalyticsRules.CompletedJobStatuses.Contains(w.Status))
            .Select(w => (double)EF.Functions.DateDiffSecond(w.StartedAt!.Value, w.CompletedAt!.Value))
            .GroupBy(_ => 1)
            .Select(g => new { Average = g.Average(), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        return AnalyticsRules.Duration(row?.Average, row?.Count ?? 0);
    }

    public async Task<RepeatedFailuresDto> RepeatedFailuresByEquipmentAsync(
        int months, int minimumRequests, int top, CancellationToken ct)
    {
        var window = Window(months);
        var threshold = Math.Max(minimumRequests, 1);

        // GROUP BY EquipmentId HAVING COUNT(*) >= @threshold: one small row per repeat offender.
        var repeats = await _db.ServiceRequests.AsNoTracking()
            .Where(r => r.CreatedAt >= window.FromUtc && r.CreatedAt < window.ToUtc
                && AnalyticsRules.FaultReportStatuses.Contains(r.Status))
            .GroupBy(r => r.EquipmentId)
            .Select(g => new { EquipmentId = g.Key, Count = g.Count(), Last = g.Max(r => r.CreatedAt) })
            .Where(g => g.Count >= threshold)
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last).ThenBy(g => g.EquipmentId)
            .ToListAsync(ct);

        var shown = repeats.Take(Math.Max(top, 0)).ToList();
        var ids = shown.Select(r => r.EquipmentId).ToList();
        var equipment = await _db.Equipment.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.SerialNumber, e.EquipmentType, e.CustomerId, e.Customer!.CompanyName })
            .ToDictionaryAsync(e => e.Id, ct);

        return new RepeatedFailuresDto
        {
            MinimumRequests = threshold,
            EquipmentCount = repeats.Count,
            Items = shown.Select(r => new RepeatedFailureDto
            {
                EquipmentId = r.EquipmentId,
                SerialNumber = equipment[r.EquipmentId].SerialNumber,
                EquipmentType = equipment[r.EquipmentId].EquipmentType,
                CustomerId = equipment[r.EquipmentId].CustomerId,
                CustomerName = equipment[r.EquipmentId].CompanyName,
                RequestCount = r.Count,
                LastReportedAt = r.Last
            }).ToList()
        };
    }

    public async Task<IReadOnlyList<PartUsageDto>> TopPartsByUsageAsync(int months, int top, CancellationToken ct)
    {
        var window = Window(months);

        // The part lines are the net record of use (a return deletes its line) and carry the price at the time.
        // Each line is rounded to sen before summing — ROUND(Quantity * UnitPrice, 2), half away from zero for
        // decimals, as StockMovementRules.LineTotal — so a part's cost here agrees with the job totals it came from.
        var usage = await _db.WorkOrderParts.AsNoTracking()
            .Where(l => l.CreatedAt >= window.FromUtc && l.CreatedAt < window.ToUtc)
            .GroupBy(l => l.PartId)
            .Select(g => new
            {
                PartId = g.Key,
                Quantity = g.Sum(l => l.Quantity),
                Cost = g.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)),
                Jobs = g.Select(l => l.WorkOrderId).Distinct().Count()
            })
            .OrderByDescending(g => g.Cost).ThenByDescending(g => g.Quantity).ThenBy(g => g.PartId)
            .Take(Math.Max(top, 0))
            .ToListAsync(ct);

        var ids = usage.Select(u => u.PartId).ToList();
        var parts = await _db.Parts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.PartNumber, p.Name, p.Unit })
            .ToDictionaryAsync(p => p.Id, ct);

        return usage.Select(u => new PartUsageDto
        {
            PartId = u.PartId,
            PartNumber = parts[u.PartId].PartNumber,
            Name = parts[u.PartId].Name,
            Unit = parts[u.PartId].Unit,
            Quantity = u.Quantity,
            Cost = u.Cost,
            JobCount = u.Jobs
        }).ToList();
    }

    public async Task<JobsWaitingForPartsDto> JobsWaitingForPartsAsync(int top, CancellationToken ct)
    {
        // Served by IX_WorkOrders_Status; a handful of paused jobs at most.
        var waiting = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == WorkOrderStatus.PendingParts && w.PendingPartId != null)
            .OrderBy(w => w.PendingPartsAt).ThenBy(w => w.Id)
            .Select(w => new JobWaitingForPartDto
            {
                WorkOrderId = w.Id,
                PartId = w.PendingPartId!.Value,
                PartNumber = w.PendingPart!.PartNumber,
                Name = w.PendingPart.Name,
                Unit = w.PendingPart.Unit,
                QuantityNeeded = w.PendingPartQuantity ?? 0,
                QuantityOnHand = w.PendingPart.QuantityOnHand,
                WaitingSince = w.PendingPartsAt,
                // The Phase 7 resume rule: at least the quantity asked for is on hand.
                InStock = w.PendingPart.QuantityOnHand >= (w.PendingPartQuantity ?? 0)
            })
            .ToListAsync(ct);

        return new JobsWaitingForPartsDto
        {
            Count = waiting.Count,
            ReadyToResume = waiting.Count(w => w.InStock),
            Items = waiting.Take(Math.Max(top, 0)).ToList()
        };
    }

    public async Task<RequestQueueDto> RequestQueueAsync(int top, int recentDays, CancellationToken ct)
    {
        var since = _clock.GetUtcNow().UtcDateTime.AddDays(-Math.Max(recentDays, 0));
        var take = Math.Max(top, 0);

        // Served by IX_ServiceRequests_Status.
        var byStatus = await _db.ServiceRequests.AsNoTracking()
            .Where(r => AnalyticsRules.OpenRequestStatuses.Contains(r.Status))
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        var oldest = await _db.ServiceRequests.AsNoTracking()
            .Where(r => AnalyticsRules.OpenRequestStatuses.Contains(r.Status))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(take)
            .Select(r => new
            {
                r.Id, r.Customer!.CompanyName, r.Equipment!.SerialNumber, r.Equipment.EquipmentType,
                r.Priority, r.Status, r.CreatedAt
            })
            .ToListAsync(ct);

        var rejected = _db.ServiceRequests.AsNoTracking()
            .Where(r => r.Status == ServiceRequestStatus.Rejected && r.RejectedAt >= since);
        var rejectedCount = await rejected.CountAsync(ct);
        var recentlyRejected = await rejected
            .OrderByDescending(r => r.RejectedAt).ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new RejectedRequestDto
            {
                Id = r.Id,
                CustomerName = r.Customer!.CompanyName,
                EquipmentSerialNumber = r.Equipment!.SerialNumber,
                RejectedAt = r.RejectedAt!.Value,
                RejectionReason = r.RejectionReason ?? string.Empty
            })
            .ToListAsync(ct);

        return new RequestQueueDto
        {
            AwaitingReview = byStatus.GetValueOrDefault(ServiceRequestStatus.New),
            AwaitingAssignment = byStatus.GetValueOrDefault(ServiceRequestStatus.Approved),
            RecentDays = recentDays,
            RejectedRecently = rejectedCount,
            OldestOpen = oldest.Select(r => new QueuedRequestDto
            {
                Id = r.Id,
                CustomerName = r.CompanyName,
                EquipmentSerialNumber = r.SerialNumber,
                EquipmentType = r.EquipmentType,
                Priority = r.Priority.ToString(),
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            }).ToList(),
            RecentlyRejected = recentlyRejected
        };
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(int months, CancellationToken ct)
    {
        // One DbContext cannot run queries concurrently, so the operations run one after another.
        var generatedAt = _clock.GetUtcNow().UtcDateTime;
        var byPriority = await OpenRequestsByPriorityAsync(ct);
        var byStatus = await WorkOrdersByStatusAsync(ct);
        var perMonth = await JobsCompletedPerMonthAsync(months, ct);
        var lowStock = await LowStockSummaryAsync(AnalyticsRules.LowStockItems, ct);

        return new DashboardSummaryDto
        {
            GeneratedAt = generatedAt,
            Period = AnalyticsRules.Period(AnalyticsRules.MonthsEnding(generatedAt, months)),
            Kpis = AnalyticsRules.Kpis(byPriority, byStatus, perMonth, lowStock),
            OpenRequestsByPriority = byPriority,
            WorkOrdersByStatus = byStatus,
            JobsCompletedPerMonth = perMonth,
            LowStock = lowStock,
            AverageTimeToAssignment = await AverageTimeToAssignmentAsync(months, ct),
            AverageCompletionDuration = await AverageCompletionDurationAsync(months, ct),
            RepeatedFailures = await RepeatedFailuresByEquipmentAsync(
                months, AnalyticsRules.DefaultRepeatedFailureThreshold, AnalyticsRules.ManagerListItems, ct),
            TopPartsByUsage = await TopPartsByUsageAsync(months, AnalyticsRules.ManagerListItems, ct)
        };
    }

    public async Task<WarehouseDashboardDto> GetWarehouseDashboardAsync(int months, CancellationToken ct)
    {
        var generatedAt = _clock.GetUtcNow().UtcDateTime;
        var activeParts = await _db.Parts.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var lowStock = await LowStockSummaryAsync(AnalyticsRules.WarehouseListItems, ct);
        var waiting = await JobsWaitingForPartsAsync(AnalyticsRules.WarehouseListItems, ct);

        return new WarehouseDashboardDto
        {
            GeneratedAt = generatedAt,
            Period = AnalyticsRules.Period(AnalyticsRules.MonthsEnding(generatedAt, months)),
            Kpis = new WarehouseKpisDto
            {
                ActiveParts = activeParts,
                LowStockParts = lowStock.Count,
                OutOfStockParts = lowStock.OutOfStock,
                JobsWaitingForParts = waiting.Count
            },
            LowStock = lowStock,
            JobsWaitingForParts = waiting,
            TopPartsByUsage = await TopPartsByUsageAsync(months, AnalyticsRules.ManagerListItems, ct)
        };
    }

    public async Task<ServiceQueueDashboardDto> GetServiceQueueDashboardAsync(CancellationToken ct)
    {
        var generatedAt = _clock.GetUtcNow().UtcDateTime;
        var queue = await RequestQueueAsync(AnalyticsRules.QueueListItems, AnalyticsRules.RecentRejectionDays, ct);

        return new ServiceQueueDashboardDto
        {
            GeneratedAt = generatedAt,
            Kpis = new ServiceQueueKpisDto
            {
                AwaitingReview = queue.AwaitingReview,
                AwaitingAssignment = queue.AwaitingAssignment,
                RejectedRecently = queue.RejectedRecently
            },
            OpenRequestsByPriority = await OpenRequestsByPriorityAsync(ct),
            Queue = queue
        };
    }
}
