using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Ai;

/// <summary>Asks the technician assistant about one job. Everything else it uses — the problem, the equipment and
/// its history — the server reads from that job, so a caller cannot point it at someone else's records.</summary>
public class TechnicianAssistantRequest
{
    [Range(1, int.MaxValue)]
    public int WorkOrderId { get; set; }

    /// <summary>Optional: what the technician has noticed on site. Sent to the model as data, never as instructions.</summary>
    [MaxLength(1000)]
    public string? Observations { get; set; }
}
