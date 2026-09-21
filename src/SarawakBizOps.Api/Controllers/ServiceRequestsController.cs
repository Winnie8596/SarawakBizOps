using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Services.ServiceRequests;

namespace SarawakBizOps.Api.Controllers;

// PRD 6.3. Reading is limited to the office roles (a Technician only ever sees their own work
// orders, BR-02); raising a request is ServiceStaff/Admin; the review decision belongs to the Manager.
[ApiController]
[Route("api/service-requests")]
[Authorize(Roles = "Admin,Manager,ServiceStaff")]
public class ServiceRequestsController : ControllerBase
{
    private readonly IServiceRequestService _service;

    public ServiceRequestsController(IServiceRequestService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ServiceRequestFilter filter, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(filter, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var request = await _service.GetByIdAsync(id, ct);
        return request is null ? NotFound() : Ok(request);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,ServiceStaff")]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequestRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ActingUserId, ct);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : this.ToProblem(result);
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(id, ActingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectServiceRequestRequest request, CancellationToken ct)
    {
        var result = await _service.RejectAsync(id, request.Reason, ActingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    private string ActingUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
