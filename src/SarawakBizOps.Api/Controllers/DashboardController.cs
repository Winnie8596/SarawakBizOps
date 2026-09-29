using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.9, SDD §14 (Phase 11): the Manager's dashboard. Admin sees it too; the other roles get their own
// dashboards in Phase 12.
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Admin,Manager")]
public class DashboardController : ControllerBase
{
    private readonly IAnalyticsService _analytics;

    public DashboardController(IAnalyticsService analytics) => _analytics = analytics;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) => Ok(await _analytics.GetSummaryAsync(ct));
}
