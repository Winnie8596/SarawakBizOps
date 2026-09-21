using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.Services.Equipment;
using SarawakBizOps.Api.Services.History;

namespace SarawakBizOps.Api.Controllers;

[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _equipmentService;

    private readonly IServiceHistoryService _historyService;

    public EquipmentController(IEquipmentService equipmentService, IServiceHistoryService historyService)
    {
        _equipmentService = equipmentService;
        _historyService = historyService;
    }

    // FR-08: GET /api/equipment?customerId=5
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? customerId, CancellationToken ct)
        => Ok(await _equipmentService.GetAllAsync(customerId, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var equipment = await _equipmentService.GetByIdAsync(id, ct);
        return equipment is null ? NotFound() : Ok(equipment);
    }

    // FR-07 — registering equipment is a Service Staff / Admin action.
    [HttpPost]
    [Authorize(Roles = "Admin,ServiceStaff")]
    public async Task<IActionResult> Create([FromBody] EquipmentRequest request, CancellationToken ct)
    {
        var (success, error, equipment) = await _equipmentService.CreateAsync(request, ct);
        if (!success)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad request", detail: error);
        }

        return CreatedAtAction(nameof(GetById), new { id = equipment!.Id }, equipment);
    }

    // FR-07 — editing equipment (including status changes) is a Service Staff / Admin action.
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,ServiceStaff")]
    public async Task<IActionResult> Update(int id, [FromBody] EquipmentUpdateRequest request, CancellationToken ct)
    {
        var result = await _equipmentService.UpdateAsync(id, request, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Office roles only: a Technician's view of history is limited to their own jobs (BR-02).
    [HttpGet("{id:int}/history")]
    [Authorize(Roles = "Admin,Manager,ServiceStaff")]
    public async Task<IActionResult> GetHistory(int id, CancellationToken ct)
    {
        var history = await _historyService.ForEquipmentAsync(id, ct);
        return history is null ? NotFound() : Ok(history);
    }
}
