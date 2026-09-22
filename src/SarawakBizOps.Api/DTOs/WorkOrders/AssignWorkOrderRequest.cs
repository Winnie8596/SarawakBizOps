using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

public class AssignWorkOrderRequest
{
    [Required]
    public string TechnicianId { get; set; } = string.Empty;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
