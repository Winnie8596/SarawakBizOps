using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.7: the Reports page lists every generated service report for the office roles. A report is generated
// and downloaded per job under /api/work-orders/{id} (generate-report, report).
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin,Manager,ServiceStaff")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _reports.GetAllAsync(ct));
}
