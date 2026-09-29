namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>
/// One photo on a job (Phase 9). The image itself is only served by the authorized
/// GET /api/work-orders/{workOrderId}/photos/{id} — no storage key or URL leaves the API.
/// </summary>
public class WorkOrderPhotoDto
{
    public int Id { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}
