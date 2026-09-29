namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.8. A photo the technician took on the job (Phase 9). The image lives in blob storage
// (IFileStorage); the database keeps only its internal key and metadata — never the bytes and never a public
// URL (SDD §15) — so it can only be read back through the authorized API.
public class WorkOrderPhoto
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string BlobKey { get; set; } = string.Empty;

    // Decided from the bytes at upload (PhotoRules), never from what the client claimed.
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }

    public string? Description { get; set; }

    public string UploadedByUserId { get; set; } = string.Empty;
    public ApplicationUser? UploadedByUser { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
