using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Inventory;

namespace SarawakBizOps.Api.Controllers;

// SDD §10. The ledger is append-only (BR-09): there is intentionally no PUT, PATCH or DELETE here —
// a mistake is corrected by posting an offsetting Adjustment with a reason.
[ApiController]
[Route("api/inventory-transactions")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
public class InventoryTransactionsController : ControllerBase
{
    private readonly IInventoryService _service;

    public InventoryTransactionsController(IInventoryService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType<CursorPage<InventoryTransactionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] InventoryTransactionFilter filter, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(filter, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Receive or Adjustment only; Issue/Return come only from work-order part operations
    // (POST/DELETE /api/work-orders/{id}/parts).
    // There is no GET-by-id, so the 201 carries the new row in its body without a Location header.
    [HttpPost]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.WarehouseStaff}")]
    [ProducesResponseType<InventoryTransactionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateInventoryTransactionRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, result.Value) : this.ToProblem(result);
    }
}
