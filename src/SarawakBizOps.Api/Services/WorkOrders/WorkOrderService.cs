using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Inventory;

namespace SarawakBizOps.Api.Services.WorkOrders;

public class WorkOrderService : IWorkOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<WorkOrderService> _logger;

    public WorkOrderService(ApplicationDbContext db, ILogger<WorkOrderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<WorkOrderDto>>> GetAllAsync(WorkOrderFilter filter, CancellationToken ct)
    {
        var query = WithDetails(_db.WorkOrders.AsNoTracking());

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!TryParseName<WorkOrderStatus>(filter.Status, out var status))
            {
                return ServiceResult<IReadOnlyList<WorkOrderDto>>.Fail(
                    ServiceErrorKind.Validation, NameListMessage<WorkOrderStatus>("Status"));
            }
            query = query.Where(w => w.Status == status);
        }

        var rows = await query
            .OrderByDescending(w => w.AssignedAt)
            .ThenByDescending(w => w.Id)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<WorkOrderDto>>.Ok(rows.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<WorkOrderDto>> GetMineAsync(string technicianId, CancellationToken ct)
    {
        var rows = await WithDetails(_db.WorkOrders.AsNoTracking())
            .Where(w => w.TechnicianId == technicianId)
            .OrderByDescending(w => w.AssignedAt)
            .ThenByDescending(w => w.Id)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<WorkOrderDto>> GetByIdAsync(
        int id, string actingUserId, bool requireOwnTechnician, CancellationToken ct)
    {
        var row = await WithDetails(_db.WorkOrders.AsNoTracking()).FirstOrDefaultAsync(w => w.Id == id, ct);
        if (row is null || (requireOwnTechnician && row.TechnicianId != actingUserId))
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        return ServiceResult<WorkOrderDto>.Ok(ToDto(row));
    }

    public async Task<ServiceResult<WorkOrderDto>> AssignAsync(
        int serviceRequestId, string technicianId, string actingUserId, byte[] rowVersion, CancellationToken ct)
    {
        var request = await _db.ServiceRequests.AsNoTracking()
            .Where(r => r.Id == serviceRequestId)
            .Select(r => new { r.Status })
            .FirstOrDefaultAsync(ct);
        if (request is null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
        }

        if (request.Status != ServiceRequestStatus.Approved)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, AssignRefusal(request.Status));
        }

        var technician = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == technicianId, ct);
        if (technician is null || !technician.IsActive)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation,
                "Choose an active technician to assign this job to.");
        }

        var hasTechnicianRole = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.Id
                where ur.UserId == technicianId && r.Name == AppRoles.Technician
                select 1)
            .AnyAsync(ct);
        if (!hasTechnicianRole)
        {
            // BR-11: only a user with the Technician role may be assigned.
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation,
                "The selected user is not a Technician.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Same conditional-UPDATE guard as ServiceRequestService.TransitionAsync: only a request still
        // Approved *and* still carrying the RowVersion the caller last saw moves, so two Managers racing
        // to assign the same request cannot both succeed.
        var moved = await _db.ServiceRequests
            .Where(r => r.Id == serviceRequestId && r.Status == ServiceRequestStatus.Approved && r.RowVersion == rowVersion)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ServiceRequestStatus.Assigned), ct);

        if (moved == 0)
        {
            await tx.RollbackAsync(ct);
            var latest = await CurrentRequestStatusAsync(serviceRequestId, ct);
            if (latest is null)
            {
                return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
            }

            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
                latest.Value != ServiceRequestStatus.Approved
                    ? AssignRefusal(latest.Value)
                    : "This request was changed by someone else. Refresh and try again.");
        }

        var now = DateTime.UtcNow;
        var workOrder = new WorkOrder
        {
            ServiceRequestId = serviceRequestId,
            TechnicianId = technicianId,
            Status = WorkOrderStatus.Assigned,
            AssignedAt = now
        };
        _db.WorkOrders.Add(workOrder);

        try
        {
            // BR-12's final guard: the filtered unique index on ServiceRequestId (excluding Cancelled
            // rows) rejects a second live work order outright even if two assigns somehow both got past
            // the check above.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
                "This request already has a work order.");
        }

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "WorkOrder {WorkOrderId} created for ServiceRequest {RequestId}, assigned to {TechnicianId} by {ActingUserId}",
            workOrder.Id, serviceRequestId, technicianId, actingUserId);

        return ServiceResult<WorkOrderDto>.Ok((await GetDtoAsync(workOrder.Id, ct))!);
    }

    public Task<ServiceResult<WorkOrderDto>> StartAsync(int id, string technicianId, byte[] rowVersion, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return TransitionAsync(id, WorkOrderStateMachine.StartRefusal, technicianId, rowVersion,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkOrderStatus.InProgress)
                .SetProperty(w => w.StartedAt, now), token),
            ct);
    }

    public async Task<ServiceResult<WorkOrderDto>> MarkPendingPartsAsync(
        int id, string technicianId, string reason, int partId, decimal quantity, byte[] rowVersion, CancellationToken ct)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation,
                "Say what the job is waiting for.");
        }

        var invalid = StockMovementRules.ValidateJobQuantity(quantity);
        if (invalid is not null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        var part = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.Name, p.Unit, p.QuantityOnHand, p.IsActive })
            .FirstOrDefaultAsync(ct);
        if (part is null || !part.IsActive)
        {
            // Inactive parts are out of the catalogue the technician picks from, so they read as unknown.
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, "Choose a part from the catalogue.");
        }

        // Waiting only makes sense for stock that is not there: with enough on hand, add it to the job instead.
        if (part.QuantityOnHand >= quantity)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
                $"{part.Name} is in stock ({part.QuantityOnHand:0.###} {part.Unit} on hand); add it to the job instead of waiting.");
        }

        var now = DateTime.UtcNow;
        var result = await TransitionAsync(id, from => WorkOrderStateMachine.Refusal(from, WorkOrderStatus.PendingParts),
            technicianId, rowVersion,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkOrderStatus.PendingParts)
                .SetProperty(w => w.PendingPartsReason, trimmed)
                .SetProperty(w => w.PendingPartId, partId)
                .SetProperty(w => w.PendingPartQuantity, quantity)
                .SetProperty(w => w.PendingPartsAt, now)
                .SetProperty(w => w.ResumedAt, (DateTime?)null), token),
            ct);

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "WorkOrder {WorkOrderId} InProgress -> PendingParts by {TechnicianId}: waiting for {Quantity} of Part {PartId}",
                id, technicianId, quantity, partId);
        }

        return result;
    }

    public async Task<ServiceResult<WorkOrderDto>> ResumeAsync(int id, string technicianId, byte[] rowVersion, CancellationToken ct)
    {
        // Friendly message first; the same condition is repeated inside the UPDATE below, which is what
        // actually decides (stock can be issued elsewhere between this read and the update).
        if (await ResumeShortageAsync(id, ct) is { } shortage)
        {
            var owned = await _db.WorkOrders.AnyAsync(w => w.Id == id && w.TechnicianId == technicianId, ct);
            return owned
                ? ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, shortage)
                : ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        var now = DateTime.UtcNow;
        var result = await TransitionAsync(id, WorkOrderStateMachine.ResumeRefusal, technicianId, rowVersion,
            (rows, token) => rows
                .Where(w => w.PendingPartId == null
                    || _db.Parts.Any(p => p.Id == w.PendingPartId && p.QuantityOnHand >= w.PendingPartQuantity))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WorkOrderStatus.InProgress)
                    .SetProperty(w => w.ResumedAt, now), token),
            ct);

        if (!result.Succeeded && result.ErrorKind == ServiceErrorKind.Conflict
            && await ResumeShortageAsync(id, ct) is { } lateShortage)
        {
            // Lost the race for the stock between the check above and the update.
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, lateShortage);
        }

        if (result.Succeeded)
        {
            _logger.LogInformation("WorkOrder {WorkOrderId} PendingParts -> InProgress by {TechnicianId}", id, technicianId);
        }

        return result;
    }

    // Why a PendingParts job cannot resume yet, or null when its awaited part has enough stock.
    private async Task<string?> ResumeShortageAsync(int id, CancellationToken ct)
    {
        var waiting = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id && w.Status == WorkOrderStatus.PendingParts && w.PendingPart != null)
            .Select(w => new
            {
                w.PendingPart!.Name,
                w.PendingPart.Unit,
                w.PendingPart.QuantityOnHand,
                Needed = w.PendingPartQuantity ?? 0
            })
            .FirstOrDefaultAsync(ct);

        return waiting is null || waiting.QuantityOnHand >= waiting.Needed
            ? null
            : $"Still waiting for parts: {waiting.Needed:0.###} {waiting.Unit} of {waiting.Name} needed, " +
              $"{waiting.QuantityOnHand:0.###} on hand.";
    }

    public async Task<ServiceResult<WorkOrderDto>> UpdateDiagnosisAsync(
        int id, string technicianId, string diagnosis, string workPerformed, string? notes,
        byte[] rowVersion, CancellationToken ct)
    {
        var current = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.Status, w.TechnicianId })
            .FirstOrDefaultAsync(ct);

        if (current is null || current.TechnicianId != technicianId)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        // Central lock guard: a locked (Approved/Cancelled) job refuses any mutating call, not just a
        // status change — checked the same way here as everywhere else, instead of a bespoke inline check.
        if (WorkOrderStateMachine.IsLocked(current.Status))
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
                WorkOrderStateMachine.LockedMessage(current.Status));
        }

        if (current.Status != WorkOrderStatus.InProgress)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
                $"This job is {current.Status}; diagnosis can only be recorded while it is in progress.");
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var updated = await _db.WorkOrders
            .Where(w => w.Id == id && w.Status == WorkOrderStatus.InProgress && w.RowVersion == rowVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Diagnosis, diagnosis.Trim())
                .SetProperty(w => w.WorkPerformed, workPerformed.Trim())
                .SetProperty(w => w.Notes, trimmedNotes), ct);

        if (updated == 0)
        {
            // Covers both races: the job left InProgress, or someone else's edit already moved the
            // RowVersion on — either way this is the lost-update case the token exists to catch.
            var latest = await CurrentStatusAsync(id, ct);
            var message = latest is not null && latest.Value != WorkOrderStatus.InProgress
                ? $"This job is {latest.Value}; diagnosis can only be recorded while it is in progress."
                : "This job was changed by someone else. Refresh and try again.";
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, message);
        }

        return ServiceResult<WorkOrderDto>.Ok((await GetDtoAsync(id, ct))!);
    }

    public async Task<ServiceResult<WorkOrderDto>> CompleteAsync(int id, string technicianId, byte[] rowVersion, CancellationToken ct)
    {
        var current = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.Status, w.TechnicianId, w.Diagnosis, w.WorkPerformed, w.SignatureBlobKey })
            .FirstOrDefaultAsync(ct);

        if (current is null || current.TechnicianId != technicianId)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        // BR-03, AC-05/AC-06: checked ahead of the transition guard so a job that is otherwise ready to
        // complete gets a message about the missing evidence, not about its status.
        if (current.Status == WorkOrderStatus.InProgress)
        {
            if (string.IsNullOrWhiteSpace(current.Diagnosis) || string.IsNullOrWhiteSpace(current.WorkPerformed))
            {
                return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation,
                    "Enter a diagnosis and the work performed before completing this job.");
            }

            if (current.SignatureBlobKey is null)
            {
                return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation,
                    "Capture the customer's signature before completing this job.");
            }
        }

        var now = DateTime.UtcNow;
        return await TransitionAsync(id, from => WorkOrderStateMachine.Refusal(from, WorkOrderStatus.Completed),
            technicianId, rowVersion,
            // The signature is repeated in the WHERE so completion can never land on a row without one.
            (rows, token) => rows
                .Where(w => w.SignatureBlobKey != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WorkOrderStatus.Completed)
                    .SetProperty(w => w.CompletedAt, now), token),
            ct);
    }

    public Task<ServiceResult<WorkOrderDto>> ApproveAsync(int id, string actingUserId, byte[] rowVersion, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return TransitionAsync(id, from => WorkOrderStateMachine.Refusal(from, WorkOrderStatus.Approved), null, rowVersion,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkOrderStatus.Approved)
                .SetProperty(w => w.ApprovedAt, now), token),
            ct);
    }

    public async Task<ServiceResult<WorkOrderDto>> CancelAsync(
        int id, string reason, string actingUserId, byte[] rowVersion, CancellationToken ct)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation,
                "A reason is required when cancelling a work order.");
        }

        var current = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.Status, w.ServiceRequestId })
            .FirstOrDefaultAsync(ct);
        if (current is null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        var refusal = WorkOrderStateMachine.Refusal(current.Status, WorkOrderStatus.Cancelled);
        if (refusal is not null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, refusal);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var from = current.Status;
        var now = DateTime.UtcNow;
        var updated = await _db.WorkOrders
            .Where(w => w.Id == id && w.Status == from && w.RowVersion == rowVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkOrderStatus.Cancelled)
                .SetProperty(w => w.CancellationReason, trimmed)
                .SetProperty(w => w.CancelledByUserId, actingUserId)
                .SetProperty(w => w.CancelledAt, now), ct);

        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            var latest = await CurrentStatusAsync(id, ct);
            if (latest is null)
            {
                return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
            }

            var message = latest.Value != from
                ? WorkOrderStateMachine.Refusal(latest.Value, WorkOrderStatus.Cancelled)
                    ?? "This job was changed by someone else. Refresh and try again."
                : "This job was changed by someone else. Refresh and try again.";
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, message);
        }

        // Decision: cancelling a work order returns its request to Approved so it re-enters the assign
        // queue — a side effect of the cancel, done here in the same transaction, not a manager-facing
        // ServiceRequest transition. Only relevant once the request actually reached Assigned; a request
        // still New (job cancelled before assignment even settled) is left alone.
        await _db.ServiceRequests
            .Where(r => r.Id == current.ServiceRequestId && r.Status == ServiceRequestStatus.Assigned)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ServiceRequestStatus.Approved), ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "WorkOrder {WorkOrderId} cancelled ({From} -> Cancelled) by {UserId}: {Reason}",
            id, from, actingUserId, trimmed);

        return ServiceResult<WorkOrderDto>.Ok((await GetDtoAsync(id, ct))!);
    }

    public async Task<IReadOnlyList<TechnicianSummaryDto>> GetTechniciansAsync(CancellationToken ct)
    {
        var technicianIds = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.Id
                where r.Name == AppRoles.Technician
                select ur.UserId)
            .ToListAsync(ct);

        var technicians = await _db.Users.AsNoTracking()
            .Where(u => technicianIds.Contains(u.Id) && u.IsActive)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var openCounts = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.TechnicianId != null
                && (w.Status == WorkOrderStatus.Assigned || w.Status == WorkOrderStatus.InProgress
                    || w.Status == WorkOrderStatus.PendingParts))
            .GroupBy(w => w.TechnicianId!)
            .Select(g => new { TechnicianId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TechnicianId, x => x.Count, ct);

        return technicians.Select(t => new TechnicianSummaryDto
        {
            Id = t.Id,
            FullName = t.FullName,
            OpenJobCount = openCounts.GetValueOrDefault(t.Id, 0)
        }).ToList();
    }

    /// <summary>
    /// Runs one guarded status change, mirroring ServiceRequestService.TransitionAsync: the state
    /// machine gives a clear message, then a single conditional UPDATE ... WHERE Status = @from AND
    /// RowVersion = @rowVersion means two people racing on the same job — or a status change racing a
    /// non-status edit — cannot both succeed: the loser updates 0 rows and gets a 409. A non-null
    /// <paramref name="technicianId"/> also enforces BR-02 ownership, hiding someone else's job as
    /// not-found rather than exposing it with a 403. <paramref name="refusal"/> says why the action is not
    /// allowed from a given status (null when it is).
    /// </summary>
    private async Task<ServiceResult<WorkOrderDto>> TransitionAsync(
        int id, Func<WorkOrderStatus, string?> refusal, string? technicianId, byte[] rowVersion,
        Func<IQueryable<WorkOrder>, CancellationToken, Task<int>> apply, CancellationToken ct)
    {
        var current = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.Status, w.TechnicianId })
            .FirstOrDefaultAsync(ct);

        if (current is null || (technicianId is not null && current.TechnicianId != technicianId))
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        if (refusal(current.Status) is { } refused)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, refused);
        }

        var from = current.Status;
        var updated = await apply(
            _db.WorkOrders.Where(w => w.Id == id && w.Status == from && w.RowVersion == rowVersion), ct);
        if (updated == 0)
        {
            var latest = await CurrentStatusAsync(id, ct);
            if (latest is null)
            {
                return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
            }

            var message = latest.Value != from
                ? refusal(latest.Value) ?? "This job was changed by someone else. Refresh and try again."
                : "This job was changed by someone else. Refresh and try again.";

            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, message);
        }

        return ServiceResult<WorkOrderDto>.Ok((await GetDtoAsync(id, ct))!);
    }

    private async Task<WorkOrderStatus?> CurrentStatusAsync(int id, CancellationToken ct)
        => await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => (WorkOrderStatus?)w.Status)
            .FirstOrDefaultAsync(ct);

    private async Task<ServiceRequestStatus?> CurrentRequestStatusAsync(int id, CancellationToken ct)
        => await _db.ServiceRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (ServiceRequestStatus?)r.Status)
            .FirstOrDefaultAsync(ct);

    private static string AssignRefusal(ServiceRequestStatus status)
        => $"This request is {status} and cannot be assigned. Only an Approved request can be assigned to a technician.";

    private async Task<WorkOrderDto?> GetDtoAsync(int id, CancellationToken ct)
    {
        var row = await WithDetails(_db.WorkOrders.AsNoTracking()).FirstOrDefaultAsync(w => w.Id == id, ct);
        return row is null ? null : ToDto(row);
    }

    private static IQueryable<WorkOrder> WithDetails(IQueryable<WorkOrder> query) => query
        .Include(w => w.ServiceRequest!).ThenInclude(r => r.Customer)
        .Include(w => w.ServiceRequest!).ThenInclude(r => r.Equipment)
        .Include(w => w.Technician)
        .Include(w => w.CancelledByUser)
        .Include(w => w.PendingPart)
        .Include(w => w.PartsUsed).ThenInclude(wp => wp.Part)
        .Include(w => w.Photos).ThenInclude(p => p.UploadedByUser)
        .AsSplitQuery();

    private static bool TryParseName<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out result);
    }

    private static string NameListMessage<TEnum>(string field) where TEnum : struct, Enum
        => $"{field} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";

    private static WorkOrderDto ToDto(WorkOrder w)
    {
        var parts = w.PartsUsed
            .OrderBy(wp => wp.CreatedAt)
            .ThenBy(wp => wp.Id)
            .Select(wp => new WorkOrderPartDto
            {
                Id = wp.Id,
                PartId = wp.PartId,
                PartNumber = wp.Part?.PartNumber ?? string.Empty,
                PartName = wp.Part?.Name ?? string.Empty,
                Unit = wp.Part?.Unit ?? string.Empty,
                Quantity = wp.Quantity,
                UnitPrice = wp.UnitPrice,
                LineTotal = StockMovementRules.LineTotal(wp.Quantity, wp.UnitPrice),
                CreatedAt = wp.CreatedAt
            })
            .ToList();

        var dto = ToDtoWithoutParts(w);
        dto.Parts = parts;
        dto.PartsTotal = parts.Sum(p => p.LineTotal);
        dto.Photos = w.Photos
            .OrderBy(p => p.UploadedAt)
            .ThenBy(p => p.Id)
            .Select(p => new WorkOrderPhotoDto
            {
                Id = p.Id,
                ContentType = p.ContentType,
                SizeBytes = p.SizeBytes,
                UploadedByName = p.UploadedByUser?.FullName ?? string.Empty,
                UploadedAt = p.UploadedAt
            })
            .ToList();
        return dto;
    }

    private static WorkOrderDto ToDtoWithoutParts(WorkOrder w) => new()
    {
        Id = w.Id,
        ServiceRequestId = w.ServiceRequestId,
        CustomerId = w.ServiceRequest?.CustomerId ?? 0,
        CustomerName = w.ServiceRequest?.Customer?.CompanyName ?? string.Empty,
        EquipmentId = w.ServiceRequest?.EquipmentId ?? 0,
        EquipmentSerialNumber = w.ServiceRequest?.Equipment?.SerialNumber ?? string.Empty,
        EquipmentType = w.ServiceRequest?.Equipment?.EquipmentType ?? string.Empty,
        ProblemDescription = w.ServiceRequest?.ProblemDescription ?? string.Empty,
        Priority = w.ServiceRequest?.Priority.ToString() ?? string.Empty,
        TechnicianId = w.TechnicianId,
        TechnicianName = w.Technician?.FullName ?? string.Empty,
        Status = w.Status.ToString(),
        AssignedAt = w.AssignedAt,
        StartedAt = w.StartedAt,
        CompletedAt = w.CompletedAt,
        ApprovedAt = w.ApprovedAt,
        Diagnosis = w.Diagnosis,
        WorkPerformed = w.WorkPerformed,
        Notes = w.Notes,
        HasSignature = w.SignatureBlobKey != null,
        SignedAt = w.SignedAt,
        CancelledByName = w.CancelledByUser?.FullName,
        CancelledAt = w.CancelledAt,
        CancellationReason = w.CancellationReason,
        PendingPartsReason = w.PendingPartsReason,
        PendingPartId = w.PendingPartId,
        PendingPartNumber = w.PendingPart?.PartNumber,
        PendingPartName = w.PendingPart?.Name,
        PendingPartUnit = w.PendingPart?.Unit,
        PendingPartQuantity = w.PendingPartQuantity,
        PendingPartsAt = w.PendingPartsAt,
        ResumedAt = w.ResumedAt,
        RowVersion = Convert.ToBase64String(w.RowVersion ?? Array.Empty<byte>())
    };
}
