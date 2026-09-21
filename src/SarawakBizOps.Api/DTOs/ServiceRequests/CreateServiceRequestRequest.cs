using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.ServiceRequests;

// The creator is never taken from the body: it comes from the token (BR-08).
public class CreateServiceRequestRequest
{
    [Required]
    public int CustomerId { get; set; }

    [Required]
    public int EquipmentId { get; set; }

    [Required, MaxLength(2000)]
    public string ProblemDescription { get; set; } = string.Empty;

    /// <summary>Low, Medium, High or Urgent. Defaults to Medium when omitted.</summary>
    [MaxLength(20)]
    public string? Priority { get; set; }
}
