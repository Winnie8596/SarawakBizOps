namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>For the assign dialog's technician picker (PRD §6.1).</summary>
public class TechnicianSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>Jobs currently Assigned or InProgress — a quick load indicator when choosing who to assign.</summary>
    public int OpenJobCount { get; set; }
}
