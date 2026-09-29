namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.9. Generated from the Completed work-order snapshot (Phase 10); Manager approval
// afterward locks the work order and the report version (BR-14 per D-05). One report per work order: a
// regeneration replaces the file and bumps Revision but keeps the ReportNumber.
public class ServiceReport
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    // SR-{year}-{sequence}, drawn once from the ServiceReportNumbers SEQUENCE; unique.
    public string ReportNumber { get; set; } = string.Empty;

    // 1 for the first rendering, +1 for each regeneration while the job is still Completed.
    public int Revision { get; set; } = 1;

    // The PDF lives in blob storage (IFileStorage); the database keeps only its internal key and size — never
    // the bytes or a public URL (SDD §15) — so it can only be read back through the authorized API.
    public string BlobKey { get; set; } = string.Empty;
    public int SizeBytes { get; set; }

    public string GeneratedByUserId { get; set; } = string.Empty;
    public ApplicationUser? GeneratedByUser { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
