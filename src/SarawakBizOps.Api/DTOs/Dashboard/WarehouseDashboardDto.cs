namespace SarawakBizOps.Api.DTOs.Dashboard;

/// <summary>
/// GET /api/dashboard/summary for WarehouseStaff (Phase 12, PRD §5): stock, what jobs are waiting for, and what
/// the jobs use. No work-order statuses, request queue or turnaround — that is the Manager's view.
/// </summary>
public class WarehouseDashboardDto
{
    public string View { get; set; } = DashboardViews.Warehouse;

    public DateTime GeneratedAt { get; set; }

    /// <summary>The Malaysia-time months <see cref="TopPartsByUsage"/> covers.</summary>
    public AnalyticsPeriodDto Period { get; set; } = new();

    public WarehouseKpisDto Kpis { get; set; } = new();

    /// <summary>The same rule and order as the Manager's low-stock panel and GET /api/parts/low-stock, a longer list.</summary>
    public LowStockSummaryDto LowStock { get; set; } = new();

    public JobsWaitingForPartsDto JobsWaitingForParts { get; set; } = new();

    public IReadOnlyList<PartUsageDto> TopPartsByUsage { get; set; } = Array.Empty<PartUsageDto>();
}

public class WarehouseKpisDto
{
    public int ActiveParts { get; set; }
    public int LowStockParts { get; set; }
    public int OutOfStockParts { get; set; }
    public int JobsWaitingForParts { get; set; }
}

public class JobsWaitingForPartsDto
{
    /// <summary>Every job paused as PendingParts.</summary>
    public int Count { get; set; }

    /// <summary>Of those, the ones whose part is now in stock in the quantity asked for: the technician can resume.</summary>
    public int ReadyToResume { get; set; }

    /// <summary>The longest-waiting first.</summary>
    public IReadOnlyList<JobWaitingForPartDto> Items { get; set; } = Array.Empty<JobWaitingForPartDto>();
}

public class JobWaitingForPartDto
{
    public int WorkOrderId { get; set; }
    public int PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityNeeded { get; set; }
    public decimal QuantityOnHand { get; set; }

    /// <summary>UTC; when the technician paused the job.</summary>
    public DateTime? WaitingSince { get; set; }

    /// <summary>Enough on hand for the technician to resume (the Phase 7 resume rule).</summary>
    public bool InStock { get; set; }
}
