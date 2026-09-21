using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs.Customers;
using SarawakBizOps.Api.Services.Customers;

namespace SarawakBizOps.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _customerService.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var customer = await _customerService.GetByIdAsync(id, ct);
        return customer is null ? NotFound() : Ok(customer);
    }

    // FR-04 — creating customers is a Service Staff / Admin action.
    // Role names are written as plain literals here (matching AppRoles'
    // values) rather than interpolated into the attribute, since attribute
    // arguments must be compile-time constants.
    [HttpPost]
    [Authorize(Roles = "Admin,ServiceStaff")]
    public async Task<IActionResult> Create([FromBody] CustomerRequest request, CancellationToken ct)
    {
        var customer = await _customerService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
    }

    // FR-05
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,ServiceStaff")]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerRequest request, CancellationToken ct)
    {
        var updated = await _customerService.UpdateAsync(id, request, ct);
        return updated ? NoContent() : NotFound();
    }
}
