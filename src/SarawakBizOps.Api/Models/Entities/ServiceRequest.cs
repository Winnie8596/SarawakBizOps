using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.3.
// CustomerId is intentionally stored even though it is reachable through Equipment.
// The service layer must verify Equipment.CustomerId == CustomerId at creation time (BR-10).
public class ServiceRequest
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public string ProblemDescription { get; set; } = string.Empty;
    public RequestPriority Priority { get; set; } = RequestPriority.Medium;
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.New;

    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Set together when a Manager rejects the request; the reason is mandatory (PRD §6.3).
    public string? RejectionReason { get; set; }
    public string? RejectedByUserId { get; set; }
    public ApplicationUser? RejectedByUser { get; set; }
    public DateTime? RejectedAt { get; set; }

    // 1:1 for MVP — BR-12: a ServiceRequest can produce at most one WorkOrder.
    public WorkOrder? WorkOrder { get; set; }
}
