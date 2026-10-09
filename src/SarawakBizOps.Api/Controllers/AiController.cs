using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SarawakBizOps.Api.DTOs.Ai;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.RateLimiting;
using SarawakBizOps.Api.Services.Ai;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.10, SDD §14 (Phases 13–14). Advice only: these endpoints read, never write (AiReadOnlyData). A provider failure
// or an unusable answer is a 503 the UI shows as "AI unavailable"; asking too often is a 429 (per-user rate limit).
[ApiController]
[Route("api/ai")]
[EnableRateLimiting(RateLimitPolicies.Ai)]
public class AiController : ControllerBase
{
    private readonly IAiService _ai;

    public AiController(IAiService ai) => _ai = ai;

    // AI-01. The job's own Technician (another technician's job is a 404, BR-02) or a Manager.
    [HttpPost("technician-assistant")]
    [Authorize(Roles = $"{AppRoles.Technician},{AppRoles.Manager}")]
    [ProducesResponseType<TechnicianAssistanceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TechnicianAssistant([FromBody] TechnicianAssistantRequest request, CancellationToken ct)
    {
        var result = await _ai.GetTechnicianAssistanceAsync(
            request.WorkOrderId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, User.IsInRole(AppRoles.Technician),
            request.Observations, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // AI-02 (Phase 14). What the Manager AI can be asked about; no model call, so no rate limit.
    [HttpGet("manager-insights/topics")]
    [Authorize(Roles = AppRoles.Manager)]
    [DisableRateLimiting]
    [ProducesResponseType<IReadOnlyList<ManagerInsightTopicDto>>(StatusCodes.Status200OK)]
    public IActionResult ManagerInsightTopics() => Ok(_ai.GetManagerInsightTopics());

    // AI-02, AC-10. Always 200: an answer (figures from the analytics, and the model's wording of them when it
    // could give one) or a refusal listing the supported topics.
    [HttpPost("manager-insights")]
    [Authorize(Roles = AppRoles.Manager)]
    [ProducesResponseType<ManagerInsightDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ManagerInsights([FromBody] ManagerInsightsRequest request, CancellationToken ct)
        => Ok(await _ai.AskManagerInsightsAsync(request.Question, ct));
}
