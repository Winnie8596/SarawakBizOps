using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.ServiceRequests;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;

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

    public Task<ServiceResult<ServiceRequestDto>> ApproveAsync(int id, string actingUserId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return TransitionAsync(id, ServiceRequestStatus.Approved, actingUserId,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ServiceRequestStatus.Approved)
                .SetProperty(r => r.ApprovedByUserId, actingUserId)
                .SetProperty(r => r.ApprovedAt, now), token),
            ct);
    }

    public Task<ServiceResult<ServiceRequestDto>> RejectAsync(
        int id, string reason, string actingUserId, CancellationToken ct)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
        {
            return Task.FromResult(ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Validation,
                "A reason is required when rejecting a request."));
        }

        var now = DateTime.UtcNow;
        return TransitionAsync(id, ServiceRequestStatus.Rejected, actingUserId,
            (rows, token) => rows.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ServiceRequestStatus.Rejected)
                .SetProperty(r => r.RejectionReason, trimmed)
                .SetProperty(r => r.RejectedByUserId, actingUserId)
                .SetProperty(r => r.RejectedAt, now), token),
            ct);
    }

    /// <summary>
    /// Runs one guarded status change. The state machine gives a clear message for a request that is
    /// already past New; the UPDATE itself is conditional on the status we validated (WHERE Status = @from),
    /// so two Managers acting on the same request cannot both succeed: the loser updates 0 rows and gets a
    /// 409 instead of silently overwriting the other decision.
    /// </summary>
    private async Task<ServiceResult<ServiceRequestDto>> TransitionAsync(
        int id, ServiceRequestStatus target, string actingUserId,
        Func<IQueryable<ServiceRequest>, CancellationToken, Task<int>> apply, CancellationToken ct)
    {
        var current = await CurrentStatusAsync(id, ct);
        if (current is null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
        }

        var refusal = ServiceRequestStateMachine.Refusal(current.Value, target);
        if (refusal is not null)
        {
            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Conflict, refusal);
        }

        var from = current.Value;
        var updated = await apply(_db.ServiceRequests.Where(r => r.Id == id && r.Status == from), ct);
        if (updated == 0)
        {
            // Someone else moved it between our read and our write: report the state it is in now.
            var latest = await CurrentStatusAsync(id, ct);
            if (latest is null)
            {
                return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.NotFound, "Service request not found.");
            }

            return ServiceResult<ServiceRequestDto>.Fail(ServiceErrorKind.Conflict,
                ServiceRequestStateMachine.Refusal(latest.Value, target)
                ?? "This request was changed by someone else. Refresh and try again.");
        }

        _logger.LogInformation(
            "ServiceRequest {RequestId} moved {From} -> {To} by {UserId}", id, from, target, actingUserId);

        return ServiceResult<ServiceRequestDto>.Ok((await GetByIdAsync(id, ct))!);
    }

    private async Task<ServiceRequestStatus?> CurrentStatusAsync(int id, CancellationToken ct)
        => await _db.ServiceRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (ServiceRequestStatus?)r.Status)
            .FirstOrDefaultAsync(ct);

    private static IQueryable<ServiceRequest> WithDetails(IQueryable<ServiceRequest> query) => query
        .Include(r => r.Customer)
        .Include(r => r.Equipment)
        .Include(r => r.CreatedByUser)
        .Include(r => r.ApprovedByUser)
        .Include(r => r.RejectedByUser);

    // Enum.TryParse would also accept "1" or " Low"; only the exact names are valid API values.
    private static bool TryParseName<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out result);
    }

    private static string NameListMessage<TEnum>(string field) where TEnum : struct, Enum
        => $"{field} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";

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
        RejectionReason = r.RejectionReason
    };
}
