using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.9, §5, SDD §14: one endpoint, a role-shaped payload (Phase 12). Manager and Admin get the full dashboard
// (Phase 11's, deepened), WarehouseStaff the stock view, ServiceStaff the request queue; each payload carries only
// its own view's numbers and says which it is in "view". A Technician has My Jobs instead and gets 403.
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff},{AppRoles.ServiceStaff}")]
public class DashboardController : ControllerBase
{
    private readonly IAnalyticsService _analytics;

    public DashboardController(IAnalyticsService analytics) => _analytics = analytics;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) => AnalyticsRules.ViewFor(User.IsInRole) switch
    {
        DashboardViews.Manager => Ok(await _analytics.GetSummaryAsync(ct)),
        DashboardViews.Warehouse => Ok(await _analytics.GetWarehouseDashboardAsync(ct)),
        DashboardViews.ServiceQueue => Ok(await _analytics.GetServiceQueueDashboardAsync(ct)),
        _ => Forbid()
    };
}
