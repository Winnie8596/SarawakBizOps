using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Controllers;

// Backs the Manager's assign dialog (PRD §6.1 technician picker with open-job counts).
[ApiController]
[Route("api/technicians")]
[Authorize(Roles = AppRoles.Manager)]
public class TechniciansController : ControllerBase
{
    private readonly IWorkOrderService _service;

    public TechniciansController(IWorkOrderService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TechnicianSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetTechniciansAsync(ct));
}
