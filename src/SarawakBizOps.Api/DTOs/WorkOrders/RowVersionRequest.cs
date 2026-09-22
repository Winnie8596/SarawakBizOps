using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>Body for a work-order action that changes nothing but status (start/complete/approve):
/// just the conflict token, so the caller still proves they last saw the record in its current state.</summary>
public class RowVersionRequest
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
