using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>Pauses a job until a part is in stock: resume is refused until that part has
/// <see cref="Quantity"/> on hand.</summary>
public class PendingPartsRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public int PartId { get; set; }

    public decimal Quantity { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
