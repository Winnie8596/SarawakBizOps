using SarawakBizOps.Api.DTOs.Ai;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The AI features (PRD §6.10). Advice only: nothing here can change business data (the only database access is
/// <see cref="AiReadOnlyData"/>). Provider trouble — an error, a timeout, an answer that fails validation — comes back
/// as <see cref="ServiceErrorKind.Unavailable"/>, never as an exception.
/// </summary>
public interface IAiService
{
    /// <summary>AI-01. <paramref name="requireOwnTechnician"/> is true for a Technician: another technician's job is
    /// reported as not found (BR-02), as everywhere else.</summary>
    Task<ServiceResult<TechnicianAssistanceDto>> GetTechnicianAssistanceAsync(
        int workOrderId, string actingUserId, bool requireOwnTechnician, string? observations, CancellationToken ct);
}
