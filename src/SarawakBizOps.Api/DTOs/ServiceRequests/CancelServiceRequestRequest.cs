using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.ServiceRequests;

public class CancelServiceRequestRequest : IRowVersionRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
