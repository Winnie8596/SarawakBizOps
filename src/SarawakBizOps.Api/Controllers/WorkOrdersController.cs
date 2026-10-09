using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Reports;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Controllers;

// PRD §6.4. Office roles monitor every job; a Technician only ever sees and acts on their own
// (BR-02, AC-02) — enforced in the service layer, since a plain [Authorize] cannot express ownership.
[ApiController]
[Route("api/work-orders")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.ServiceStaff},{AppRoles.Technician}")]
public class WorkOrdersController : ControllerBase
{
    private readonly IWorkOrderService _service;
    private readonly IWorkOrderPartService _parts;
    private readonly IWorkOrderSignatureService _signatures;
    private readonly IWorkOrderPhotoService _photos;
    private readonly IReportService _reports;

    public WorkOrdersController(
        IWorkOrderService service, IWorkOrderPartService parts, IWorkOrderSignatureService signatures,
        IWorkOrderPhotoService photos, IReportService reports)
    {
        _service = service;
        _parts = parts;
        _signatures = signatures;
        _photos = photos;
        _reports = reports;
    }

    [HttpGet]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.ServiceStaff}")]
    [ProducesResponseType<IReadOnlyList<WorkOrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] WorkOrderFilter filter, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(filter, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<IReadOnlyList<WorkOrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _service.GetMineAsync(ActingUserId, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ActingUserId, User.IsInRole(AppRoles.Technician), ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/start")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.StartAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPatch("{id:int}/diagnosis")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDiagnosis(int id, [FromBody] DiagnosisUpdateRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.UpdateDiagnosisAsync(
            id, ActingUserId, request.Diagnosis, request.WorkPerformed, request.Notes, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // ---- Phase 7: parts used on the job (AC-03/AC-04) and PendingParts ----

    [HttpPost("{id:int}/parts")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPart(int id, [FromBody] AddWorkOrderPartRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _parts.AddAsync(id, ActingUserId, request.PartId, request.Quantity, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // {lineId} is the WorkOrderPart line (the same part can be on a job in more than one line). The whole
    // line goes back to stock; the body carries the job's rowVersion like every other mutating call.
    [HttpDelete("{id:int}/parts/{lineId:int}")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReturnPart(int id, int lineId, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _parts.ReturnAsync(id, lineId, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/pending-parts")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PendingParts(int id, [FromBody] PendingPartsRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.MarkPendingPartsAsync(
            id, ActingUserId, request.Reason, request.PartId, request.Quantity, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/resume")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.ResumeAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // ---- Phase 8: the customer's signature (BR-03, AC-06) ----

    // Small on purpose: a canvas signature is tens of KB. The limit leaves room for the multipart framing, and
    // anything larger is refused before the body is buffered.
    private const long SignatureRequestLimit = SignatureRules.MaxBytes + 16 * 1024;

    [HttpPost("{id:int}/signature")]
    [Authorize(Roles = AppRoles.Technician)]
    [RequestSizeLimit(SignatureRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = SignatureRequestLimit)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadSignature(int id, [FromForm] SignatureUploadRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        if (request.File is null || request.File.Length == 0)
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation,
                "The signature is empty. Ask the customer to sign, then save it."));
        }

        if (request.File.Length > SignatureRules.MaxBytes)
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation,
                $"The signature image is too large (maximum {SignatureRules.MaxBytes / 1024} KB)."));
        }

        // The declared Content-Type and file name are ignored: SignatureRules checks the bytes themselves.
        using var buffer = new MemoryStream((int)request.File.Length);
        await request.File.CopyToAsync(buffer, ct);

        var result = await _signatures.SaveAsync(id, ActingUserId, buffer.ToArray(), rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Same audience as GET /{id}: office roles see any job's signature; a Technician only their own (another
    // technician's job reads as 404). There is no public URL — this endpoint is the only way to the image.
    [HttpGet("{id:int}/signature")]
    public async Task<IActionResult> GetSignature(int id, CancellationToken ct)
    {
        var result = await _signatures.OpenAsync(id, ActingUserId, User.IsInRole(AppRoles.Technician), ct);
        if (!result.Succeeded)
        {
            return this.ToProblem(result);
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, SignatureRules.ContentType);
    }

    // ---- Phase 9: photos taken on the job (PRD §6.6) ----

    // One photo per request (the web app uploads a queue one by one, each carrying the rowVersion the last
    // one returned). The limit leaves room for the multipart framing; anything larger is refused unbuffered.
    private const long PhotoRequestLimit = PhotoRules.MaxBytes + 64 * 1024;

    [HttpPost("{id:int}/photos")]
    [Authorize(Roles = AppRoles.Technician)]
    [RequestSizeLimit(PhotoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestLimit)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadPhoto(int id, [FromForm] PhotoUploadRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        if (request.File is null || request.File.Length == 0)
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation,
                "The photo is empty. Take or choose a photo, then upload it."));
        }

        if (request.File.Length > PhotoRules.MaxBytes)
        {
            return this.ToProblem(ServiceResult.Fail(ServiceErrorKind.Validation,
                $"The photo is too large (maximum {PhotoRules.MaxBytes / (1024 * 1024)} MB)."));
        }

        // The declared Content-Type and file name are ignored: PhotoRules decides the type from the bytes.
        // ASP.NET has already spooled anything over 64 KB of the form to a temporary file; the photo is read from
        // there into one array of its exact size (docs/decisions.md: buffered, not streamed, at 8 MB a photo).
        var image = new byte[request.File.Length];
        await using (var upload = request.File.OpenReadStream())
        {
            await upload.ReadExactlyAsync(image, ct);
        }

        var result = await _photos.AddAsync(id, ActingUserId, image, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Before completion only; the body carries the job's rowVersion like every other mutating call.
    [HttpDelete("{id:int}/photos/{photoId:int}")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemovePhoto(int id, int photoId, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _photos.RemoveAsync(id, photoId, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Same audience as GET /{id} (and the signature): office roles see any job's photos; a Technician only
    // their own. There is no public URL — this endpoint is the only way to the image.
    [HttpGet("{id:int}/photos/{photoId:int}")]
    public async Task<IActionResult> GetPhoto(int id, int photoId, CancellationToken ct)
    {
        var result = await _photos.OpenAsync(id, photoId, ActingUserId, User.IsInRole(AppRoles.Technician), ct);
        if (!result.Succeeded)
        {
            return this.ToProblem(result);
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    // ---- Phase 10: the service report PDF (PRD §6.7, BR-14 per D-05) ----

    // No rowVersion: generating documents the job without changing it. The service re-checks the job's state under
    // its row lock, so a report can never be written after the job was approved with one.
    [HttpPost("{id:int}/generate-report")]
    [Authorize(Roles = AppRoles.Manager)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateReport(int id, CancellationToken ct)
    {
        var result = await _reports.GenerateAsync(id, ActingUserId, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Same audience as GET /{id}: office roles see any job's report; a Technician only their own (another
    // technician's job reads as 404). There is no public URL — this endpoint is the only way to the PDF.
    [HttpGet("{id:int}/report")]
    public async Task<IActionResult> GetReport(int id, CancellationToken ct)
    {
        var result = await _reports.OpenAsync(id, ActingUserId, User.IsInRole(AppRoles.Technician), ct);
        if (!result.Succeeded)
        {
            return this.ToProblem(result);
        }

        var (file, reportNumber) = result.Value;
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, ReportRules.ContentType, $"{reportNumber}.pdf");
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = AppRoles.Technician)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.CompleteAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = AppRoles.Manager)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(int id, [FromBody] RowVersionRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.ApproveAsync(id, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    // Manager only, mirroring approve (plan's demo script: "a Manager cancels an assigned job").
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = AppRoles.Manager)]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelWorkOrderRequest request, CancellationToken ct)
    {
        var rowVersion = request.RowVersionBytes();

        var result = await _service.CancelAsync(id, request.Reason, ActingUserId, rowVersion, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    private string ActingUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
