using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
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
    private readonly IWorkOrderPartService _parts;

    public WorkOrdersController(IWorkOrderService service, IWorkOrderPartService parts)
    {
        _service = service;
        _parts = parts;
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
    public async Task<IActionResult> Start(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.StartAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPatch("{id:int}/diagnosis")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> UpdateDiagnosis(int id, [FromBody] DiagnosisUpdateRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.UpdateDiagnosisAsync(
            id, ActingUserId, request.Diagnosis, request.WorkPerformed, request.Notes, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // ---- Phase 7: parts used on the job (AC-03/AC-04) and PendingParts ----

    [HttpPost("{id:int}/parts")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> AddPart(int id, [FromBody] AddWorkOrderPartRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _parts.AddAsync(id, ActingUserId, request.PartId, request.Quantity, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // {lineId} is the WorkOrderPart line (the same part can be on a job in more than one line). The whole
    // line goes back to stock; the body carries the job's rowVersion like every other mutating call.
    [HttpDelete("{id:int}/parts/{lineId:int}")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> ReturnPart(int id, int lineId, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _parts.ReturnAsync(id, lineId, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/pending-parts")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> PendingParts(int id, [FromBody] PendingPartsRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.MarkPendingPartsAsync(
            id, ActingUserId, request.Reason, request.PartId, request.Quantity, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/resume")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> Resume(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.ResumeAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = "Technician")]
    public async Task<IActionResult> Complete(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.CompleteAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Approve(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.ApproveAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Manager only, mirroring approve (plan's demo script: "a Manager cancels an assigned job").
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelWorkOrderRequest request, CancellationToken ct)
    {
        if (!RowVersionCodec.TryParse(request.RowVersion, out var rowVersion))
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation, "A valid rowVersion is required."));
        }

        var result = await _service.CancelAsync(id, request.Reason, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    private string ActingUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
