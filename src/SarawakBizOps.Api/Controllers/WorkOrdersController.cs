using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.4. Office roles monitor every job; a Technician only ever sees and acts on their own
// (BR-02, AC-02) — enforced in the service layer, since a plain [Authorize] cannot express ownership.
[ApiController]
[Route("api/work-orders")]
[Authorize(Roles = "Admin,Manager,ServiceStaff,Technician")]
public class WorkOrdersController : ControllerBase
{
    private readonly IWorkOrderService _service;

    public WorkOrdersController(IWorkOrderService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Manager,ServiceStaff")]
    public async Task<IActionResult> GetAll([FromQuery] WorkOrderFilter filter, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(filter, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _service.GetMineAsync(ActingUserId, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ActingUserId, User.IsInRole(AppRoles.Technician), ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/start")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var result = await _service.StartAsync(id, ActingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPatch("{id:int}/diagnosis")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> UpdateDiagnosis(int id, [FromBody] DiagnosisUpdateRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateDiagnosisAsync(
            id, ActingUserId, request.Diagnosis, request.WorkPerformed, request.Notes, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        var result = await _service.CompleteAsync(id, ActingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(id, ActingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    private string ActingUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
