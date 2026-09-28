using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Parts;

// No opening quantity: a new part starts at zero and stock arrives through a Receive transaction,
// so QuantityOnHand always equals the sum of the ledger (BR-09).
public class CreatePartRequest
{
    [Required, MaxLength(60)]
    public string PartNumber { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required, MaxLength(20)]
    public string Unit { get; set; } = "pcs";

    public decimal UnitCost { get; set; }

    public decimal MinimumStockLevel { get; set; }
}
