using System.Linq.Expressions;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Services.Parts;

/// <summary>
/// The one definition of "low on stock" (AC-09), shared by <c>GET /api/parts/low-stock</c> and the dashboard's
/// <c>LowStockSummary</c> so the two can never disagree: an active part whose QuantityOnHand is at or below its
/// MinimumStockLevel. Inactive (discontinued) parts are never low — nobody is going to reorder them.
/// </summary>
public static class StockLevels
{
    public static readonly Expression<Func<Part, bool>> IsLow =
        p => p.IsActive && p.QuantityOnHand <= p.MinimumStockLevel;

    private static readonly Func<Part, bool> IsLowCompiled = IsLow.Compile();

    /// <summary>The same rule applied to a part already in memory (e.g. when shaping a DTO).</summary>
    public static bool IsLowStock(Part part) => IsLowCompiled(part);
}
