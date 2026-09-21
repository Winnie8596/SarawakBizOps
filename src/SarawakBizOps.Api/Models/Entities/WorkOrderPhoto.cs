namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.8. FileUrl points at blob/object storage —
// the database stores only the reference, never the image bytes (Section 15).
public class WorkOrderPhoto
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string FileUrl { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string UploadedByUserId { get; set; } = string.Empty;
    public ApplicationUser? UploadedByUser { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
