namespace SarawakBizOps.Api.DTOs.Reports;

/// <summary>
/// A job's service report as shown on the work order (Phase 10). The PDF itself is only served by the authorized
/// GET /api/work-orders/{id}/report — no storage key or URL leaves the API.
/// </summary>
public class ServiceReportSummaryDto
{
    public string ReportNumber { get; set; } = string.Empty;

    /// <summary>1 for the first rendering; each regeneration (Completed jobs only) adds one.</summary>
    public int Revision { get; set; }

    public DateTime GeneratedAt { get; set; }
    public string GeneratedByName { get; set; } = string.Empty;
    public int SizeBytes { get; set; }

    /// <summary>True once the job is Approved: the report can no longer be regenerated (BR-14 per D-05).</summary>
    public bool IsLocked { get; set; }
}
