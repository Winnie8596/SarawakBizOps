using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.9, §5, SDD §14: one endpoint per dashboard, each with one payload shape in the OpenAPI document. Manager
// and Admin get the full dashboard (Phase 11's, deepened), WarehouseStaff the stock view, ServiceStaff the request
// queue; a Manager or Admin may also read the other two. A Technician has My Jobs instead and gets 403.
// "summary" stays for the web app: it picks the caller's own view and tags it in "view".
// "months" (1–24, default 6) is the MYT period the trend, averages and usage cover; anything else is a 400.
// Payloads are cached briefly (DashboardCache).
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff},{AppRoles.ServiceStaff}")]
public class DashboardController : ControllerBase
{
    private readonly IAnalyticsService _analytics;
    private readonly DashboardCache _cache;

    public DashboardController(IAnalyticsService analytics, DashboardCache cache)
    {
        _analytics = analytics;
        _cache = cache;
    }

    /// <summary>The caller's own dashboard: a <see cref="DashboardSummaryDto"/>, <see cref="WarehouseDashboardDto"/>
    /// or <see cref="ServiceQueueDashboardDto"/>, told apart by "view". For a fixed shape, use the endpoints below.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(
        [FromQuery, Range(1, AnalyticsRules.MaxMonths)] int months = AnalyticsRules.DefaultMonths,
        CancellationToken ct = default) => AnalyticsRules.ViewFor(User.IsInRole) switch
    {
        DashboardViews.Manager => Ok(await ManagerAsync(months, ct)),
        DashboardViews.Warehouse => Ok(await WarehouseAsync(months, ct)),
        DashboardViews.ServiceQueue => Ok(await ServiceQueueAsync(ct)),
        _ => Forbid()
    };

    [HttpGet("manager")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DashboardSummaryDto>> GetManager(
        [FromQuery, Range(1, AnalyticsRules.MaxMonths)] int months = AnalyticsRules.DefaultMonths,
        CancellationToken ct = default) => await ManagerAsync(months, ct);

    [HttpGet("warehouse")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    [ProducesResponseType(typeof(WarehouseDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseDashboardDto>> GetWarehouse(
        [FromQuery, Range(1, AnalyticsRules.MaxMonths)] int months = AnalyticsRules.DefaultMonths,
        CancellationToken ct = default) => await WarehouseAsync(months, ct);

    /// <summary>The service queue has no period: it is what is waiting now, and rejections in the last 30 days.</summary>
    [HttpGet("service-queue")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.ServiceStaff}")]
    [ProducesResponseType(typeof(ServiceQueueDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceQueueDashboardDto>> GetServiceQueue(CancellationToken ct)
        => await ServiceQueueAsync(ct);

    private Task<DashboardSummaryDto> ManagerAsync(int months, CancellationToken ct)
        => _cache.GetOrComputeAsync(DashboardViews.Manager, months, () => _analytics.GetSummaryAsync(months, ct));

    private Task<WarehouseDashboardDto> WarehouseAsync(int months, CancellationToken ct)
        => _cache.GetOrComputeAsync(DashboardViews.Warehouse, months, () => _analytics.GetWarehouseDashboardAsync(months, ct));

    private Task<ServiceQueueDashboardDto> ServiceQueueAsync(CancellationToken ct)
        => _cache.GetOrComputeAsync(DashboardViews.ServiceQueue, 0, () => _analytics.GetServiceQueueDashboardAsync(ct));
}
