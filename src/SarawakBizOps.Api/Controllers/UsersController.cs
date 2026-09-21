using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Users;
using SarawakBizOps.Api.Services.Users;

namespace SarawakBizOps.Api.Controllers;

// FR-01/02 — user administration is Admin-only, enforced here and not just in the UI (BR-08).
[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _userService.GetAllAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(id, ct);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await _userService.CreateAsync(request, ct);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : this.ToProblem(result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _userService.UpdateAsync(id, request, actingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // No email in this system, so an Admin is the recovery path for a forgotten password.
    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _userService.ResetPasswordAsync(id, request.NewPassword, ct);
        return result.Succeeded ? NoContent() : this.ToProblem(result);
    }
}
