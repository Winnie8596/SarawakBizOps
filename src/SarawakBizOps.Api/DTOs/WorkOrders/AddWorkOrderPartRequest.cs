using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>
/// A technician puts a part on their job (AC-03). There is deliberately no price field: the unit price is
/// snapshotted from the catalogue on the server, never taken from the client.
/// </summary>
public class AddWorkOrderPartRequest
{
    public int PartId { get; set; }

    public decimal Quantity { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
