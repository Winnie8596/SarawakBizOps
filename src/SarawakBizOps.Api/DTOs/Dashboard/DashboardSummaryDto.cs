namespace SarawakBizOps.Api.DTOs.Dashboard;

/// <summary>
/// GET /api/dashboard/summary (Phase 11, PRD §6.9, SDD §14): every number the Manager's dashboard shows, computed
/// by the backend's named analytics operations. Metric definitions live on <c>AnalyticsRules</c> and in the README.
/// Phase 12 adds the turnaround averages, repeated failures and parts usage; this is the payload for the Manager
/// and Admin (<see cref="View"/> = "Manager"). The other roles get <see cref="WarehouseDashboardDto"/> or
/// <see cref="ServiceQueueDashboardDto"/> from the same endpoint.
/// </summary>
public class DashboardSummaryDto
{
    public string View { get; set; } = DashboardViews.Manager;

    /// <summary>UTC; the instant "this month" was measured against.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>The Malaysia-time months the trend, the averages, the failures and the parts usage cover.</summary>
    public AnalyticsPeriodDto Period { get; set; } = new();

    public DashboardKpisDto Kpis { get; set; } = new();

    /// <summary>Open (New or Approved) requests per priority, most urgent first, every priority present.</summary>
    public IReadOnlyList<CountDto> OpenRequestsByPriority { get; set; } = Array.Empty<CountDto>();

    /// <summary>All work orders per status, in lifecycle order, every status present.</summary>
    public IReadOnlyList<CountDto> WorkOrdersByStatus { get; set; } = Array.Empty<CountDto>();

    /// <summary>Jobs completed per Malaysia-time month, oldest first, ending with the current month.</summary>
    public IReadOnlyList<MonthCountDto> JobsCompletedPerMonth { get; set; } = Array.Empty<MonthCountDto>();

    public LowStockSummaryDto LowStock { get; set; } = new();

    /// <summary>Phase 12: from a request being raised to its job being assigned, over assignments in the period.</summary>
    public DurationStatDto AverageTimeToAssignment { get; set; } = new();

    /// <summary>Phase 12: from a job being started to being completed, over completions in the period.</summary>
    public DurationStatDto AverageCompletionDuration { get; set; } = new();

    public RepeatedFailuresDto RepeatedFailures { get; set; } = new();

    /// <summary>Phase 12: the parts that cost the most on jobs in the period, costliest first.</summary>
    public IReadOnlyList<PartUsageDto> TopPartsByUsage { get; set; } = Array.Empty<PartUsageDto>();
}

/// <summary>Which dashboard a payload is: the one the caller's role gets (Phase 12, PRD §5).</summary>
public static class DashboardViews
{
    public const string Manager = "Manager";
    public const string Warehouse = "Warehouse";
    public const string ServiceQueue = "ServiceQueue";
}

public class AnalyticsPeriodDto
{
    /// <summary>"yyyy-MM" (MYT), the first month covered.</summary>
    public string FromMonth { get; set; } = string.Empty;

    /// <summary>"yyyy-MM" (MYT), the last month covered: the current one.</summary>
    public string ToMonth { get; set; } = string.Empty;

    /// <summary>UTC, inclusive: 00:00 MYT on the first day of <see cref="FromMonth"/>.</summary>
    public DateTime FromUtc { get; set; }

    /// <summary>UTC, exclusive: 00:00 MYT on the first day of the month after <see cref="ToMonth"/>.</summary>
    public DateTime ToUtc { get; set; }
}

public class DurationStatDto
{
    /// <summary>The mean, in hours; null when nothing was measured.</summary>
    public double? AverageHours { get; set; }

    /// <summary>How many were measured.</summary>
    public int Count { get; set; }
}

public class RepeatedFailuresDto
{
    /// <summary>The threshold: a piece of equipment is listed with at least this many fault reports in the period.</summary>
    public int MinimumRequests { get; set; }

    /// <summary>How many pieces of equipment reach the threshold (the list may show fewer).</summary>
    public int EquipmentCount { get; set; }

    /// <summary>Most reports first, then the most recently reported.</summary>
    public IReadOnlyList<RepeatedFailureDto> Items { get; set; } = Array.Empty<RepeatedFailureDto>();
}

public class RepeatedFailureDto
{
    public int EquipmentId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int RequestCount { get; set; }

    /// <summary>UTC; when the latest of those requests was raised.</summary>
    public DateTime LastReportedAt { get; set; }
}

public class PartUsageDto
{
    public int PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    /// <summary>Net quantity used on jobs (a part returned from a job is no longer counted).</summary>
    public decimal Quantity { get; set; }

    /// <summary>RM, at the prices snapshotted when the parts were used.</summary>
    public decimal Cost { get; set; }

    /// <summary>On how many jobs.</summary>
    public int JobCount { get; set; }
}

public class DashboardKpisDto
{
    public int OpenRequests { get; set; }
    public int ActiveWorkOrders { get; set; }
    public int AwaitingApproval { get; set; }
    public int CompletedThisMonth { get; set; }
    public int LowStockParts { get; set; }
}

/// <summary>A count for one category, e.g. { "key": "Urgent", "count": 2 }.</summary>
public class CountDto
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MonthCountDto
{
    /// <summary>"yyyy-MM" in Malaysia time, e.g. "2026-09".</summary>
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class LowStockSummaryDto
{
    /// <summary>Active parts at or below their minimum stock level (AC-09) — the same rule as GET /api/parts/low-stock.</summary>
    public int Count { get; set; }

    /// <summary>Of those, the ones with nothing on hand at all.</summary>
    public int OutOfStock { get; set; }

    /// <summary>The most urgent few (furthest below their minimum first), in the low-stock list's order.</summary>
    public IReadOnlyList<LowStockItemDto> Items { get; set; } = Array.Empty<LowStockItemDto>();
}

public class LowStockItemDto
{
    public int PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal MinimumStockLevel { get; set; }
}
