namespace SarawakBizOps.Api.DTOs.ServiceRequests;

public class ServiceRequestDto
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public int EquipmentId { get; set; }
    public string EquipmentSerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;

    public string ProblemDescription { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public string? RejectedByName { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    public string? CancelledByName { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    // Set once a Manager assigns a technician (status becomes Assigned). Lets the UI link straight
    // to the job without relying on client-side state from the assign action itself.
    public int? WorkOrderId { get; set; }

    /// <summary>Base64 EF Core concurrency token — the single conflict-token transport (Phase 5):
    /// every mutating call on this request must send it back.</summary>
    public string RowVersion { get; set; } = string.Empty;
}
