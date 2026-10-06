using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Parts;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.5. WarehouseStaff/Admin run the catalogue; a Manager may read it (low stock feeds the Phase 11
// dashboard, AC-09). ServiceStaff never touch inventory. A Technician may search the active catalogue to
// pick parts for their job (Phase 7) — nothing else here.
[ApiController]
[Route("api/parts")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff},{AppRoles.Technician}")]
public class PartsController : ControllerBase
{
    private const string InventoryReaders = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}";

    private readonly IPartService _service;

    public PartsController(IPartService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PartFilter filter, CancellationToken ct)
    {
        // A technician only ever picks from parts that can actually be used on a job.
        if (User.IsInRole(AppRoles.Technician))
        {
            filter.IncludeInactive = false;
        }

        return Ok(await _service.GetAllAsync(filter, ct));
    }

    // AC-09 (API half): active parts at or below their minimum stock level.
    [HttpGet("low-stock")]
    [Authorize(Roles = InventoryReaders)]
    public async Task<IActionResult> GetLowStock(CancellationToken ct)
        => Ok(await _service.GetLowStockAsync(ct));

    [HttpGet("{id:int}")]
    [Authorize(Roles = InventoryReaders)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var part = await _service.GetByIdAsync(id, ct);
        return part is null ? NotFound() : Ok(part);
    }

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Create([FromBody] CreatePartRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : this.ToProblem(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePartRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.UpdateAsync(id, request, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }
}
