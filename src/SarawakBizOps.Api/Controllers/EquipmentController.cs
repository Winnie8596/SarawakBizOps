using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.Services.Equipment;

namespace SarawakBizOps.Api.Controllers;

[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _equipmentService;

    public EquipmentController(IEquipmentService equipmentService)
    {
        _equipmentService = equipmentService;
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
}
