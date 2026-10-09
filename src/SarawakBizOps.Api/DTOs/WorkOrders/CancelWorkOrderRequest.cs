using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

public class CancelWorkOrderRequest : IRowVersionRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
