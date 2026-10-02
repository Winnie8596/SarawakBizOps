using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SarawakBizOps.Api.DTOs.Ai;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Ai;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.10, SDD §14 (Phase 13). Advice only: these endpoints read, never write (AiReadOnlyData). A provider failure
// or an unusable answer is a 503 the UI shows as "AI unavailable"; asking too often is a 429 (per-user rate limit).
[ApiController]
[Route("api/ai")]
[EnableRateLimiting(AiServiceRegistration.RateLimitPolicy)]
public class AiController : ControllerBase
{
    private readonly IAiService _ai;

    public AiController(IAiService ai) => _ai = ai;

    // AI-01. The job's own Technician (another technician's job is a 404, BR-02) or a Manager.
    [HttpPost("technician-assistant")]
    [Authorize(Roles = $"{AppRoles.Technician},{AppRoles.Manager}")]
    public async Task<IActionResult> TechnicianAssistant([FromBody] TechnicianAssistantRequest request, CancellationToken ct)
    {
        var result = await _ai.GetTechnicianAssistanceAsync(
            request.WorkOrderId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, User.IsInRole(AppRoles.Technician),
            request.Observations, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }
}
