using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.WorkOrders;

// Saved while a job is InProgress; both fields are required before Complete will succeed (BR-03),
// but the PATCH itself just stores whatever the technician has typed so far.
public class DiagnosisUpdateRequest
{
    [Required, MaxLength(4000)]
    public string Diagnosis { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string WorkPerformed { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
