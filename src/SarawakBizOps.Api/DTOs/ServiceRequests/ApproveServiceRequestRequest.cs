using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.ServiceRequests;

public class ApproveServiceRequestRequest : IRowVersionRequest
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
