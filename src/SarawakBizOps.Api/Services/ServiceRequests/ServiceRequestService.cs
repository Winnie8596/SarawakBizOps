using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using static SarawakBizOps.Api.Services.Common.EnumNames;

namespace SarawakBizOps.Api.Services.ServiceRequests;

public class ServiceRequestService : IServiceRequestService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ServiceRequestService> _logger;

    public ServiceRequestService(ApplicationDbContext db, ILogger<ServiceRequestService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<ServiceRequestDto>>> GetAllAsync(
        ServiceRequestFilter filter, CancellationToken ct)
    {
        var query = WithDetails(_db.ServiceRequests.AsNoTracking());

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!TryParseName<ServiceRequestStatus>(filter.Status, out var status))
            {
                return ServiceResult<IReadOnlyList<ServiceRequestDto>>.Fail(
                    ServiceErrorKind.Validation, NameListMessage<ServiceRequestStatus>("Status"));
            }
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            if (!TryParseName<RequestPriority>(filter.Priority, out var priority))
            {
                return ServiceResult<IReadOnlyList<ServiceRequestDto>>.Fail(
                    ServiceErrorKind.Validation, NameListMessage<RequestPriority>("Priority"));
            }
            query = query.Where(r => r.Priority == priority);
        }

        if (filter.CustomerId.HasValue)
        {
            query = query.Where(r => r.CustomerId == filter.CustomerId.Value);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<ServiceRequestDto>>.Ok(rows.Select(ToDto).ToList());
    }

    public async Task<ServiceRequestDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var row = await WithDetails(_db.ServiceRequests.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<ServiceResult<ServiceRequestDto>> CreateAsync(
        CreateServiceRequestRequest request, string actingUserId, CancellationToken ct)
    {
        var description = request.ProblemDescription.Trim();
        if (description.Length == 0)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "Describe the problem so the Manager can review it.");
        }

        var priority = RequestPriority.Medium;
        if (!string.IsNullOrWhiteSpace(request.Priority) && !TryParseName(request.Priority, out priority))
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                NameListMessage<RequestPriority>("Priority"));
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct))
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "The selected customer does not exist.");
        }

        var equipment = await _db.Equipment.AsNoTracking()
            .Where(e => e.Id == request.EquipmentId)
            .Select(e => new { e.CustomerId, e.Status })
            .FirstOrDefaultAsync(ct);
        if (equipment is null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "The selected equipment does not exist.");
        }

        // BR-10: the equipment must be the customer's own. CustomerId is stored on the request as well,
        // so this is the one place the pairing is checked.
        if (equipment.CustomerId != request.CustomerId)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "The selected equipment does not belong to this customer.");
        }

        if (equipment.Status == EquipmentStatus.Retired)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "Retired equipment cannot be serviced. Choose other equipment, or reactivate this one first.");
        }

        var entity = new ServiceRequest
        {
            CustomerId = request.CustomerId,
            EquipmentId = request.EquipmentId,
            ProblemDescription = description,
            Priority = priority,
            Status = ServiceRequestStatus.New,
            CreatedByUserId = actingUserId
        };

        _db.ServiceRequests.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ServiceRequest {RequestId} created by {UserId} for customer {CustomerId}, equipment {EquipmentId}, priority {Priority}",
            entity.Id, actingUserId, entity.CustomerId, entity.EquipmentId, priority);

        return ServiceResult<ServiceRequestDto>.Ok((await GetByIdAsync(entity.Id, ct))!);
    }

    public Task<ServiceResult<ServiceRequestDto>> ApproveAsync(
        int id, string actingUserId, byte[] rowVersion, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return TransitionAsync(id, ServiceRequestStatus.Approved, actingUserId, rowVersion,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ServiceRequestStatus.Approved)
                .SetProperty(r => r.ApprovedByUserId, actingUserId)
                .SetProperty(r => r.ApprovedAt, now), token),
            ct);
    }

    public Task<ServiceResult<ServiceRequestDto>> RejectAsync(
        int id, string reason, string actingUserId, byte[] rowVersion, CancellationToken ct)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
        {
            return Task.FromResult(ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "A reason is required when rejecting a request."));
        }

        var now = DateTime.UtcNow;
        return TransitionAsync(id, ServiceRequestStatus.Rejected, actingUserId, rowVersion,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ServiceRequestStatus.Rejected)
                .SetProperty(r => r.RejectionReason, trimmed)
                .SetProperty(r => r.RejectedByUserId, actingUserId)
                .SetProperty(r => r.RejectedAt, now), token),
            ct);
    }

    public async Task<ServiceResult<ServiceRequestDto>> CancelAsync(
        int id, string reason, string actingUserId, bool isManager, byte[] rowVersion, CancellationToken ct)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "A reason is required when cancelling a request.");
        }

        var current = await CurrentAsync(id, ct);
        if (current is null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
        }

        var refusal = CancelRefusal(current.Status, isManager);
        if (refusal is not null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Conflict, refusal);
        }

        var from = current.Status;
        var now = DateTime.UtcNow;
        var updated = await _db.ServiceRequests
            .Where(r => r.Id == id && r.Status == from && r.RowVersion == rowVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ServiceRequestStatus.Cancelled)
                .SetProperty(r => r.CancellationReason, trimmed)
                .SetProperty(r => r.CancelledByUserId, actingUserId)
                .SetProperty(r => r.CancelledAt, now), ct);

        if (updated == 0)
        {
            var latest = await CurrentAsync(id, ct);
            if (latest is null)
            {
                return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
            }

            var message = latest.Status != from
                ? CancelRefusal(latest.Status, isManager)
                    ?? "This request was changed by someone else. Refresh and try again."
                : "This request was changed by someone else. Refresh and try again.";

            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Conflict, message);
        }

        _logger.LogInformation(
            "ServiceRequest {RequestId} cancelled ({From} -> Cancelled) by {UserId}: {Reason}",
            id, from, actingUserId, trimmed);

        return ServiceResult<ServiceRequestDto>.Ok((await GetByIdAsync(id, ct))!);
    }

    /// <summary>
    /// Cancel has its own actor-dependent rule on top of the state machine (a Manager may cancel a New
    /// or Approved request; the creator-side roles may only undo their own mistake while it is still
    /// New), so it leans on <see cref="ServiceRequestStateMachine"/> for structural validity and layers
    /// the role rule only for the Approved case.
    /// </summary>
    private static string? CancelRefusal(ServiceRequestStatus status, bool isManager)
    {
        if (!ServiceRequestStateMachine.CanTransition(status, ServiceRequestStatus.Cancelled))
        {
            return ServiceRequestStateMachine.Refusal(status, ServiceRequestStatus.Cancelled);
        }

        if (status == ServiceRequestStatus.Approved && !isManager)
        {
            return "This request is Approved. Only a Manager can cancel an Approved request.";
        }

        return null;
    }

    /// <summary>
    /// Runs one guarded status change. The state machine gives a clear message for a request that
    /// cannot reach the target from its current status; the UPDATE itself is conditional on that status
    /// and on the RowVersion the caller last saw (WHERE Status = @from AND RowVersion = @rowVersion), so
    /// two Managers acting on the same request — or a status edit racing a non-status edit — cannot both
    /// succeed: the loser updates 0 rows and gets a 409 instead of silently overwriting the other change.
    /// </summary>
    private async Task<ServiceResult<ServiceRequestDto>> TransitionAsync(
        int id, ServiceRequestStatus target, string actingUserId, byte[] rowVersion,
        Func<IQueryable<ServiceRequest>, CancellationToken, Task<int>> apply, CancellationToken ct)
    {
        var current = await CurrentAsync(id, ct);
        if (current is null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
        }

        var refusal = ServiceRequestStateMachine.Refusal(current.Status, target);
        if (refusal is not null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Conflict, refusal);
        }

        var from = current.Status;
        var updated = await apply(
            _db.ServiceRequests.Where(r => r.Id == id && r.Status == from && r.RowVersion == rowVersion), ct);
        if (updated == 0)
        {
            // Someone else moved it (or edited it) between our read and our write.
            var latest = await CurrentAsync(id, ct);
            if (latest is null)
            {
                return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
            }

            var message = latest.Status != from
                ? ServiceRequestStateMachine.Refusal(latest.Status, target)
                    ?? "This request was changed by someone else. Refresh and try again."
                : "This request was changed by someone else. Refresh and try again.";

            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Conflict, message);
        }

        _logger.LogInformation(
            "ServiceRequest {RequestId} moved {From} -> {To} by {UserId}", id, from, target, actingUserId);

        return ServiceResult<ServiceRequestDto>.Ok((await GetByIdAsync(id, ct))!);
    }

    private async Task<CurrentState?> CurrentAsync(int id, CancellationToken ct)
    {
        var row = await _db.ServiceRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Status, r.RowVersion })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new CurrentState(row.Status, row.RowVersion ?? Array.Empty<byte>());
    }

    private sealed record CurrentState(ServiceRequestStatus Status, byte[] RowVersion);

    private static IQueryable<ServiceRequest> WithDetails(IQueryable<ServiceRequest> query) => query
        .Include(r => r.Customer)
        .Include(r => r.Equipment)
        .Include(r => r.CreatedByUser)
        .Include(r => r.ApprovedByUser)
        .Include(r => r.RejectedByUser)
        .Include(r => r.CancelledByUser)
        .Include(r => r.WorkOrders);

    private static ServiceRequestDto ToDto(ServiceRequest r) => new()
    {
        Id = r.Id,
        CustomerId = r.CustomerId,
        CustomerName = r.Customer?.CompanyName ?? string.Empty,
        EquipmentId = r.EquipmentId,
        EquipmentSerialNumber = r.Equipment?.SerialNumber ?? string.Empty,
        EquipmentType = r.Equipment?.EquipmentType ?? string.Empty,
        ProblemDescription = r.ProblemDescription,
        Priority = r.Priority.ToString(),
        Status = r.Status.ToString(),
        CreatedByUserId = r.CreatedByUserId,
        CreatedByName = r.CreatedByUser?.FullName ?? string.Empty,
        CreatedAt = r.CreatedAt,
        ApprovedByName = r.ApprovedByUser?.FullName,
        ApprovedAt = r.ApprovedAt,
        RejectedByName = r.RejectedByUser?.FullName,
        RejectedAt = r.RejectedAt,
        RejectionReason = r.RejectionReason,
        CancelledByName = r.CancelledByUser?.FullName,
        CancelledAt = r.CancelledAt,
        CancellationReason = r.CancellationReason,
        WorkOrderId = r.WorkOrders.FirstOrDefault(w => w.Status != WorkOrderStatus.Cancelled)?.Id,
        CancelledWorkOrderIds = r.WorkOrders.Where(w => w.Status == WorkOrderStatus.Cancelled)
            .OrderBy(w => w.Id).Select(w => w.Id).ToList(),
        RowVersion = Convert.ToBase64String(r.RowVersion ?? Array.Empty<byte>())
    };
}
