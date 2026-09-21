namespace SarawakBizOps.Api.DTOs.History;

/// <summary>
/// Service history for a customer or a piece of equipment, newest first.
/// Service requests are included now; work orders join the same list in Phase 3.
/// </summary>
public class ServiceHistoryDto
{
    public List<ServiceHistoryItemDto> Items { get; set; } = new();
}

public class ServiceHistoryItemDto
{
    /// <summary>"ServiceRequest" (later also "WorkOrder").</summary>
    public string Type { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int EquipmentId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
