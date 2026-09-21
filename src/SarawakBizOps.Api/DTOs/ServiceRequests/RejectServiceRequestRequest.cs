using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.ServiceRequests;

public class RejectServiceRequestRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
