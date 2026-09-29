using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Inventory;

/// <summary>
/// A stock movement raised by the warehouse: <c>Receive</c> (a positive quantity) or <c>Adjustment</c>
/// (signed, non-zero, with a mandatory reason in <c>Notes</c>). Issue and Return are never created here —
/// they come only from work-order part operations.
/// </summary>
public class CreateInventoryTransactionRequest
{
    public int PartId { get; set; }

    [Required]
    public string TransactionType { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
