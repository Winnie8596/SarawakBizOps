namespace SarawakBizOps.Api.DTOs.Reports;

/// <summary>One row of the Reports page (GET /api/reports): the report and the job it documents.</summary>
public class ServiceReportDto : ServiceReportSummaryDto
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public string WorkOrderStatus { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string EquipmentSerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string TechnicianName { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}
