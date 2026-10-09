using SarawakBizOps.Api.DTOs.Reports;

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
    public string? ApprovedByName { get; set; }

    // Required before completion (BR-03); null until the technician records them.
    public string? Diagnosis { get; set; }
    public string? WorkPerformed { get; set; }
    public string? Notes { get; set; }

    // Phase 8: whether the customer has signed (BR-03). The image itself is only ever served by the
    // authorized GET /api/work-orders/{id}/signature — no storage key or URL leaves the API.
    public bool HasSignature { get; set; }
    public DateTime? SignedAt { get; set; }

    // Phase 9: photos taken on the job, oldest first. Each image is served only by the authorized
    // GET /api/work-orders/{id}/photos/{photoId}.
    public List<WorkOrderPhotoDto> Photos { get; set; } = new();

    // Phase 10: the service report, once a Manager has generated it; null before. The PDF is served only by
    // the authorized GET /api/work-orders/{id}/report.
    public ServiceReportSummaryDto? Report { get; set; }

    public string? CancelledByName { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    // Phase 7: parts used (oldest first) and their RM total, and the last PendingParts wait, if any.
    public List<WorkOrderPartDto> Parts { get; set; } = new();
    public decimal PartsTotal { get; set; }

    public string? PendingPartsReason { get; set; }
    public int? PendingPartId { get; set; }
    public string? PendingPartNumber { get; set; }
    public string? PendingPartName { get; set; }
    public string? PendingPartUnit { get; set; }
    public decimal? PendingPartQuantity { get; set; }
    public DateTime? PendingPartsAt { get; set; }
    public DateTime? ResumedAt { get; set; }

    /// <summary>Base64 EF Core concurrency token — the single conflict-token transport (Phase 5):
    /// every mutating call on this job must send it back.</summary>
    public string RowVersion { get; set; } = string.Empty;
}
