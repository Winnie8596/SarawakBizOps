using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Services.WorkOrders;

public class WorkOrderPhotoService : IWorkOrderPhotoService
{
    private const string ChangedMessage = "This job was changed by someone else. Refresh and try again.";

    private static readonly string LimitMessage =
        $"This job already has {PhotoRules.MaxPhotosPerJob} photos, the most it can hold. Remove one to add another.";

    private readonly ApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IWorkOrderService _workOrders;
    private readonly ILogger<WorkOrderPhotoService> _logger;

    public WorkOrderPhotoService(
        ApplicationDbContext db, IFileStorage storage, IWorkOrderService workOrders, ILogger<WorkOrderPhotoService> logger)
    {
        _db = db;
        _storage = storage;
        _workOrders = workOrders;
        _logger = logger;
    }

    public async Task<ServiceResult<WorkOrderDto>> AddAsync(
        int workOrderId, string technicianId, byte[] image, byte[] rowVersion, CancellationToken ct)
    {
        var (contentType, invalid) = PhotoRules.Validate(image);
        if (invalid is not null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        var current = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.Status, w.TechnicianId, PhotoCount = w.Photos.Count() })
            .FirstOrDefaultAsync(ct);
        if (current is null || current.TechnicianId != technicianId)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        if (Refusal(current.Status, "added") is { } refused)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, refused);
        }

        if (current.PhotoCount >= PhotoRules.MaxPhotosPerJob)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, LimitMessage);
        }

        // As for the signature (Phase 8): the file goes up first under a key nothing points at yet, then the
        // database records it. Storage and SQL cannot share a transaction, so a failed upload changes nothing
        // and a lost database write deletes the file it just uploaded — never a row pointing at a missing file.
        var key = PhotoRules.NewKey(workOrderId, contentType!);
        using (var content = new MemoryStream(image, writable: false))
        {
            await _storage.SaveAsync(key, content, contentType!, ct);
        }

        var photo = new WorkOrderPhoto
        {
            WorkOrderId = workOrderId,
            BlobKey = key,
            ContentType = contentType!,
            SizeBytes = image.Length,
            UploadedByUserId = technicianId,
            UploadedAt = DateTime.UtcNow
        };

        var committing = false;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // The limit is repeated inside the claim, under the job row's lock, so two uploads cannot both
            // squeeze past it (and the rowVersion already lets only one of two same-screen uploads through).
            if (!await ClaimJobAsync(workOrderId, technicianId, rowVersion,
                    w => w.Photos.Count() < PhotoRules.MaxPhotosPerJob, ct))
            {
                await tx.RollbackAsync(ct);
                await DeleteQuietlyAsync(key, workOrderId);
                return await LostJobRaceAsync(workOrderId, technicianId, "added", ct);
            }

            _db.WorkOrderPhotos.Add(photo);
            await _db.SaveChangesAsync(ct);
            committing = true;
            await tx.CommitAsync(ct);
        }
        catch when (!committing)
        {
            // Nothing points at the file. (A failed commit is ambiguous — the row may have landed — so the
            // file is kept then: an orphan at worst, never a row pointing at a deleted file.)
            await DeleteQuietlyAsync(key, workOrderId);
            throw;
        }

        _logger.LogInformation(
            "WorkOrder {WorkOrderId}: photo {PhotoId} ({ContentType}, {Bytes} bytes) added by {TechnicianId}",
            workOrderId, photo.Id, contentType, image.Length, technicianId);

        return await _workOrders.GetByIdAsync(workOrderId, technicianId, requireOwnTechnician: true, ct);
    }

    public async Task<ServiceResult<WorkOrderDto>> RemoveAsync(
        int workOrderId, int photoId, string technicianId, byte[] rowVersion, CancellationToken ct)
    {
        var job = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.Status, w.TechnicianId })
            .FirstOrDefaultAsync(ct);
        if (job is null || job.TechnicianId != technicianId)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        if (Refusal(job.Status, "removed") is { } refused)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, refused);
        }

        var key = await _db.WorkOrderPhotos.AsNoTracking()
            .Where(p => p.Id == photoId && p.WorkOrderId == workOrderId)
            .Select(p => p.BlobKey)
            .FirstOrDefaultAsync(ct);
        if (key is null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "That photo is not on this job.");
        }

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            if (!await ClaimJobAsync(workOrderId, technicianId, rowVersion, _ => true, ct))
            {
                await tx.RollbackAsync(ct);
                return await LostJobRaceAsync(workOrderId, technicianId, "removed", ct);
            }

            var removed = await _db.WorkOrderPhotos
                .Where(p => p.Id == photoId && p.WorkOrderId == workOrderId)
                .ExecuteDeleteAsync(ct);
            if (removed == 0)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, ChangedMessage);
            }

            await tx.CommitAsync(ct);
        }

        // Only after the row is gone: until then the job still pointed at the file.
        await DeleteQuietlyAsync(key, workOrderId);

        _logger.LogInformation("WorkOrder {WorkOrderId}: photo {PhotoId} removed by {TechnicianId}",
            workOrderId, photoId, technicianId);

        return await _workOrders.GetByIdAsync(workOrderId, technicianId, requireOwnTechnician: true, ct);
    }

    public async Task<ServiceResult<StoredFile>> OpenAsync(
        int workOrderId, int photoId, string actingUserId, bool requireOwnTechnician, CancellationToken ct)
    {
        var job = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.TechnicianId })
            .FirstOrDefaultAsync(ct);
        if (job is null || (requireOwnTechnician && job.TechnicianId != actingUserId))
        {
            return ServiceResult<StoredFile>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        var photo = await _db.WorkOrderPhotos.AsNoTracking()
            .Where(p => p.Id == photoId && p.WorkOrderId == workOrderId)
            .Select(p => new { p.BlobKey, p.ContentType })
            .FirstOrDefaultAsync(ct);
        if (photo is null)
        {
            return ServiceResult<StoredFile>.Fail(ServiceErrorKind.NotFound, "That photo is not on this job.");
        }

        var file = await _storage.OpenReadAsync(photo.BlobKey, ct);
        if (file is null)
        {
            _logger.LogWarning("WorkOrder {WorkOrderId} photo {PhotoId} points at {Key}, which is missing from storage",
                workOrderId, photoId, photo.BlobKey);
            return ServiceResult<StoredFile>.Fail(ServiceErrorKind.NotFound, "That photo could not be found.");
        }

        // Served with the type recorded from the bytes at upload, not whatever storage says.
        return ServiceResult<StoredFile>.Ok(file with { ContentType = photo.ContentType });
    }

    /// <summary>
    /// The Phase 5 conflict guard applied to a photo change (as for parts, Phase 7): a no-op UPDATE of the job
    /// that matches only while it is still this technician's, still InProgress and still at the caller's
    /// RowVersion. Matching bumps the RowVersion and holds the job row's lock until commit, so a Manager's
    /// cancel or the technician's own Complete cannot interleave with the change.
    /// </summary>
    private async Task<bool> ClaimJobAsync(
        int workOrderId, string technicianId, byte[] rowVersion,
        Expression<Func<WorkOrder, bool>> alsoRequire, CancellationToken ct)
        => await _db.WorkOrders
            .Where(w => w.Id == workOrderId
                        && w.TechnicianId == technicianId
                        && w.Status == WorkOrderStatus.InProgress
                        && w.RowVersion == rowVersion)
            .Where(alsoRequire)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, w => w.Status), ct) == 1;

    private async Task<ServiceResult<WorkOrderDto>> LostJobRaceAsync(
        int workOrderId, string technicianId, string verb, CancellationToken ct)
    {
        var latest = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId && w.TechnicianId == technicianId)
            .Select(w => new { w.Status, PhotoCount = w.Photos.Count() })
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        var message = Refusal(latest.Status, verb)
            ?? (verb == "added" && latest.PhotoCount >= PhotoRules.MaxPhotosPerJob ? LimitMessage : ChangedMessage);
        return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, message);
    }

    // The central lock guard first, so an Approved/Cancelled job gets the same "locked" message as every other
    // mutating call; then "in progress only" — which is also what keeps a completed job's photos in place.
    private static string? Refusal(WorkOrderStatus status, string verb)
    {
        if (WorkOrderStateMachine.IsLocked(status))
        {
            return WorkOrderStateMachine.LockedMessage(status);
        }

        return WorkOrderStateMachine.AcceptsPhotoChanges(status)
            ? null
            : $"This job is {status}; photos can only be {verb} while it is in progress.";
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
            _logger.LogWarning(ex, "Could not delete unused photo {Key} of WorkOrder {WorkOrderId}", key, workOrderId);
        }
    }
}
