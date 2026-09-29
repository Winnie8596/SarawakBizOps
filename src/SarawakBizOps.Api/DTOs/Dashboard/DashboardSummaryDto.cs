namespace SarawakBizOps.Api.DTOs.Dashboard;

/// <summary>
/// GET /api/dashboard/summary (Phase 11, PRD §6.9, SDD §14): every number the Manager's dashboard shows, computed
/// by the backend's named analytics operations. Metric definitions live on <c>AnalyticsRules</c> and in the README.
/// </summary>
public class DashboardSummaryDto
{
    /// <summary>UTC; the instant "this month" was measured against.</summary>
    public DateTime GeneratedAt { get; set; }

    public DashboardKpisDto Kpis { get; set; } = new();

    /// <summary>Open (New or Approved) requests per priority, most urgent first, every priority present.</summary>
    public IReadOnlyList<CountDto> OpenRequestsByPriority { get; set; } = Array.Empty<CountDto>();

    /// <summary>All work orders per status, in lifecycle order, every status present.</summary>
    public IReadOnlyList<CountDto> WorkOrdersByStatus { get; set; } = Array.Empty<CountDto>();

    /// <summary>Jobs completed per Malaysia-time month, oldest first, ending with the current month.</summary>
    public IReadOnlyList<MonthCountDto> JobsCompletedPerMonth { get; set; } = Array.Empty<MonthCountDto>();

    public LowStockSummaryDto LowStock { get; set; } = new();
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
