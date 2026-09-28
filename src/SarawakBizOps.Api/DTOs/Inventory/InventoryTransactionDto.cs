namespace SarawakBizOps.Api.DTOs.Inventory;

public class InventoryTransactionDto
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>Signed: positive adds stock, negative removes it.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The part's stock level straight after this movement (returned on create only).</summary>
    public decimal? QuantityOnHandAfter { get; set; }

    public int? WorkOrderId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
