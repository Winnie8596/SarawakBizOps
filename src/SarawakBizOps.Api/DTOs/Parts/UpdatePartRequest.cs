using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Parts;

// QuantityOnHand is deliberately absent: stock changes only through inventory transactions (BR-09).
public class UpdatePartRequest : IRowVersionRequest
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

    public bool IsActive { get; set; } = true;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
