using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>multipart/form-data body of POST /api/work-orders/{id}/signature: the PNG drawn on the canvas
/// plus the job's conflict token (Phase 5: every mutating call carries it).</summary>
public class SignatureUploadRequest
{
    public IFormFile? File { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
