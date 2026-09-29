using System.ComponentModel.DataAnnotations;
using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.4 + Section 9 (lifecycle).
public class WorkOrder
{
    public int Id { get; set; }

    public int ServiceRequestId { get; set; }
    public ServiceRequest? ServiceRequest { get; set; }

    // Optional until a Manager assigns a technician (BR-01).
    public string? TechnicianId { get; set; }
    public ApplicationUser? Technician { get; set; }

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.New;

    public DateTime? AssignedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Required before completion (BR-03) — enforced in the service layer,
    // not a database NOT NULL, so the row can exist mid-job.
    public string? Diagnosis { get; set; }
    public string? WorkPerformed { get; set; }
    public string? Notes { get; set; }

    // The customer's signature (Phase 8, BR-03): required before completion. The image lives in blob
    // storage (IFileStorage); the database keeps only its internal key and metadata, never the bytes or a
    // public URL (SDD §15), so it can only be read back through the authorized API.
    public string? SignatureBlobKey { get; set; }
    public int? SignatureSizeBytes { get; set; }
    public DateTime? SignedAt { get; set; }

    // Set together when a Manager cancels the job (Phase 5); the reason is mandatory. Cancelling
    // reverts the linked ServiceRequest to Approved (decided in Phase 5) so it can be reassigned.
    public string? CancellationReason { get; set; }
    public string? CancelledByUserId { get; set; }
    public ApplicationUser? CancelledByUser { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Set when the technician pauses the job as PendingParts (Phase 7): what they are waiting for and why.
    // Resume is allowed only once that part has at least PendingPartQuantity on hand. The fields are kept
    // after a resume as the record of the last wait; pausing again overwrites them.
    public string? PendingPartsReason { get; set; }
    public int? PendingPartId { get; set; }
    public Part? PendingPart { get; set; }
    public decimal? PendingPartQuantity { get; set; }
    public DateTime? PendingPartsAt { get; set; }
    public DateTime? ResumedAt { get; set; }

    // Optimistic concurrency token: protects state-transition and inventory
    // writes against two people acting on the same work order at once
    // (design doc Section 10 / Section 18 concurrency requirements).
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<WorkOrderPart> PartsUsed { get; set; } = new List<WorkOrderPart>();
    public ICollection<WorkOrderPhoto> Photos { get; set; } = new List<WorkOrderPhoto>();
    public ServiceReport? ServiceReport { get; set; }
}
