namespace SarawakBizOps.Api.DTOs.WorkOrders;

public class WorkOrderDto
{
    public int Id { get; set; }
    public int ServiceRequestId { get; set; }

    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public int EquipmentId { get; set; }
    public string EquipmentSerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;

    public string ProblemDescription { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;

    public string? TechnicianId { get; set; }
    public string TechnicianName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    public DateTime? AssignedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Required before completion (BR-03); null until the technician records them.
    public string? Diagnosis { get; set; }
    public string? WorkPerformed { get; set; }
    public string? Notes { get; set; }

    /// <summary>Base64 EF Core concurrency token. Not yet required on writes — Phase 5 adds that guard.</summary>
    public string RowVersion { get; set; } = string.Empty;
}
