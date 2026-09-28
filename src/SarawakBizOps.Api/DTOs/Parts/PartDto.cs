namespace SarawakBizOps.Api.DTOs.Parts;

public class PartDto
{
    public int Id { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal MinimumStockLevel { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Active and at or below its minimum (the same rule as GET /api/parts/low-stock).</summary>
    public bool IsLowStock { get; set; }

    public DateTime CreatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
