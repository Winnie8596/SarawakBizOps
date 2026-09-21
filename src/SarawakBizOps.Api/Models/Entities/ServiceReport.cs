namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.9. Generated from the Completed work-order snapshot;
// Manager approval afterward locks the work order and report version.
public class ServiceReport
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string ReportNumber { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
