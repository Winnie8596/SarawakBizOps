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

    /// <summary>AI-02 (Phase 14): the topics a Manager can ask about, with an example question each.</summary>
    IReadOnlyList<ManagerInsightTopicDto> GetManagerInsightTopics();

    /// <summary>
    /// AI-02 (Phase 14, AC-10). Maps the question to one allow-listed analytics operation by rules, runs it through
    /// the read-only data access, and asks the model only to word the resulting figures. An unsupported or
    /// ambiguous question is a <see cref="ManagerInsightOutcomes.Refused"/> answer. A model failure is not an error
    /// here: the figures are returned without a summary.
    /// </summary>
    Task<ManagerInsightDto> AskManagerInsightsAsync(string question, CancellationToken ct);
}
