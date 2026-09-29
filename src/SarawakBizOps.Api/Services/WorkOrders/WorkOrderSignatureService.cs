using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Services.WorkOrders;

public class WorkOrderSignatureService : IWorkOrderSignatureService
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IWorkOrderService _workOrders;
    private readonly ILogger<WorkOrderSignatureService> _logger;

    public WorkOrderSignatureService(
        ApplicationDbContext db, IFileStorage storage, IWorkOrderService workOrders, ILogger<WorkOrderSignatureService> logger)
    {
        _db = db;
        _storage = storage;
        _workOrders = workOrders;
        _logger = logger;
    }

    public async Task<ServiceResult<WorkOrderDto>> SaveAsync(
        int workOrderId, string technicianId, byte[] png, byte[] rowVersion, CancellationToken ct)
    {
        if (SignatureRules.Validate(png) is { } invalid)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        var current = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.Status, w.TechnicianId, w.SignatureBlobKey })
            .FirstOrDefaultAsync(ct);
        if (current is null || current.TechnicianId != technicianId)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        if (Refusal(current.Status) is { } refused)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, refused);
        }

        // The file goes up first, under a key nothing points at yet; only then does one conditional UPDATE
        // (the Phase 5 guard: owner, InProgress, and the rowVersion the technician last saw) switch the job
        // over to it. Storage and the database cannot share a transaction, so the failure cases are handled
        // explicitly: a failed upload changes nothing, and a lost UPDATE deletes the file it just uploaded.
        var key = SignatureRules.NewKey(workOrderId);
        using (var content = new MemoryStream(png, writable: false))
        {
            await _storage.SaveAsync(key, content, SignatureRules.ContentType, ct);
        }

        var now = DateTime.UtcNow;
        var updated = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.TechnicianId == technicianId
                && w.Status == WorkOrderStatus.InProgress && w.RowVersion == rowVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.SignatureBlobKey, key)
                .SetProperty(w => w.SignatureSizeBytes, png.Length)
                .SetProperty(w => w.SignedAt, now), ct);

        if (updated == 0)
        {
            await DeleteQuietlyAsync(key, workOrderId);
            var latest = await _db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == workOrderId)
                .Select(w => (WorkOrderStatus?)w.Status)
                .FirstOrDefaultAsync(ct);
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
                (latest is { } status ? Refusal(status) : null)
                    ?? "This job was changed by someone else. Refresh and try again.");
        }

        // The UPDATE matched the rowVersion the technician last saw, so nothing wrote to the row between their
        // read and ours: the key read above is exactly the one just replaced, and nothing points at it any more.
        if (current.SignatureBlobKey is { } replaced)
        {
            await DeleteQuietlyAsync(replaced, workOrderId);
        }

        _logger.LogInformation(
            "WorkOrder {WorkOrderId} signature stored by {TechnicianId} ({Bytes} bytes, replaced an earlier one: {Replaced})",
            workOrderId, technicianId, png.Length, current.SignatureBlobKey is not null);

        return await _workOrders.GetByIdAsync(workOrderId, technicianId, requireOwnTechnician: true, ct);
    }

    public async Task<ServiceResult<StoredFile>> OpenAsync(
        int workOrderId, string actingUserId, bool requireOwnTechnician, CancellationToken ct)
    {
        var row = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.TechnicianId, w.SignatureBlobKey })
            .FirstOrDefaultAsync(ct);
        if (row is null || (requireOwnTechnician && row.TechnicianId != actingUserId))
        {
            return ServiceResult<StoredFile>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        var file = row.SignatureBlobKey is null ? null : await _storage.OpenReadAsync(row.SignatureBlobKey, ct);
        if (file is null)
        {
            if (row.SignatureBlobKey is not null)
            {
                _logger.LogWarning("WorkOrder {WorkOrderId} points at signature {Key}, which is missing from storage",
                    workOrderId, row.SignatureBlobKey);
            }
            return ServiceResult<StoredFile>.Fail(ServiceErrorKind.NotFound, "This job has no customer signature yet.");
        }

        return ServiceResult<StoredFile>.Ok(file);
    }

    // Why the job cannot take a signature in this state, or null when it can. The central lock guard comes
    // first so an Approved/Cancelled job gets the same "locked" message as every other mutating call.
    private static string? Refusal(WorkOrderStatus status)
    {
        if (WorkOrderStateMachine.IsLocked(status))
        {
            return WorkOrderStateMachine.LockedMessage(status);
        }

        return status == WorkOrderStatus.InProgress
            ? null
            : $"This job is {status}; the customer signs while the job is in progress.";
    }

    // Cleanup after the database has already decided; a failure here leaves an orphaned file (logged), never
    // a job pointing at a missing one, so it must not turn a correct response into an error.
    private async Task DeleteQuietlyAsync(string key, int workOrderId)
    {
        try
        {
            await _storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete unused signature {Key} of WorkOrder {WorkOrderId}", key, workOrderId);
        }
    }
}
