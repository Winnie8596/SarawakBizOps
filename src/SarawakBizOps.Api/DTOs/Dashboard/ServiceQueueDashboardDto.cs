namespace SarawakBizOps.Api.DTOs.Dashboard;

/// <summary>
/// GET /api/dashboard/summary for ServiceStaff (Phase 12, PRD §5): the request queue they feed — what is waiting
/// for review or assignment, the longest waits, and recent rejections to follow up with the customer.
/// </summary>
public class ServiceQueueDashboardDto
{
    public string View { get; set; } = DashboardViews.ServiceQueue;

    public DateTime GeneratedAt { get; set; }

    public ServiceQueueKpisDto Kpis { get; set; } = new();

    /// <summary>The same series as the Manager's (New or Approved, per priority).</summary>
    public IReadOnlyList<CountDto> OpenRequestsByPriority { get; set; } = Array.Empty<CountDto>();

    public RequestQueueDto Queue { get; set; } = new();
}

public class ServiceQueueKpisDto
{
    public int AwaitingReview { get; set; }
    public int AwaitingAssignment { get; set; }
    public int RejectedRecently { get; set; }
}

public class RequestQueueDto
{
    /// <summary>New: waiting for a Manager to approve or reject.</summary>
    public int AwaitingReview { get; set; }

    /// <summary>Approved: waiting for a technician to be assigned.</summary>
    public int AwaitingAssignment { get; set; }

    /// <summary>How far back <see cref="RejectedRecently"/> looks.</summary>
    public int RecentDays { get; set; }

    public int RejectedRecently { get; set; }

    /// <summary>Open requests, the longest-waiting first.</summary>
    public IReadOnlyList<QueuedRequestDto> OldestOpen { get; set; } = Array.Empty<QueuedRequestDto>();

    /// <summary>Rejected within <see cref="RecentDays"/>, the most recent first.</summary>
    public IReadOnlyList<RejectedRequestDto> RecentlyRejected { get; set; } = Array.Empty<RejectedRequestDto>();
}

public class QueuedRequestDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string EquipmentSerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class RejectedRequestDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string EquipmentSerialNumber { get; set; } = string.Empty;
    public DateTime RejectedAt { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
}
