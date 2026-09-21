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
    public string? CustomerSignatureUrl { get; set; }

    // Optimistic concurrency token: protects state-transition and inventory
    // writes against two people acting on the same work order at once
    // (design doc Section 10 / Section 18 concurrency requirements).
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<WorkOrderPart> PartsUsed { get; set; } = new List<WorkOrderPart>();
    public ICollection<WorkOrderPhoto> Photos { get; set; } = new List<WorkOrderPhoto>();
    public ServiceReport? ServiceReport { get; set; }
}
