using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Inventory;

namespace SarawakBizOps.Api.Services.WorkOrders;

public class WorkOrderPartService : IWorkOrderPartService
{
    private const string ChangedMessage = "This job was changed by someone else. Refresh and try again.";

    private readonly ApplicationDbContext _db;
    private readonly IWorkOrderService _workOrders;
    private readonly ILogger<WorkOrderPartService> _logger;

    public WorkOrderPartService(ApplicationDbContext db, IWorkOrderService workOrders, ILogger<WorkOrderPartService> logger)
    {
        _db = db;
        _workOrders = workOrders;
        _logger = logger;
    }

    public async Task<ServiceResult<WorkOrderDto>> AddAsync(
        int workOrderId, string technicianId, int partId, decimal quantity, byte[] rowVersion, CancellationToken ct)
    {
        var invalid = StockMovementRules.ValidateJobQuantity(quantity);
        if (invalid is not null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        if (await CheckJobAsync(workOrderId, technicianId, "added", ct) is { } jobRefusal)
        {
            return jobRefusal;
        }

        var part = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.IsActive })
            .FirstOrDefaultAsync(ct);
        if (part is null || !part.IsActive)
        {
            // Inactive parts are out of the catalogue the technician picks from, so they read as unknown.
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, "Choose a part from the catalogue.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (!await _db.ClaimInProgressJobAsync(workOrderId, technicianId, rowVersion, ct))
        {
            await tx.RollbackAsync(ct);
            return await LostJobRaceAsync(workOrderId, technicianId, "added", ct);
        }

        // Open Question 8, decided: the same single conditional UPDATE as warehouse movements (Phase 6). The
        // database computes the new balance from its current value and re-checks the predicate under the
        // row's update lock, so N technicians racing for the last units serialise on the Part row and only
        // as many as there is stock for match a row — no RowVersion read, no retry loop, never negative.
        var issued = await _db.Parts
            .Where(p => p.Id == partId && p.IsActive && p.QuantityOnHand - quantity >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.QuantityOnHand, p => p.QuantityOnHand - quantity), ct);

        if (issued == 0)
        {
            await tx.RollbackAsync(ct);
            return await ShortageAsync(partId, quantity, ct);
        }

        // Read inside the transaction, after the UPDATE: this row is now locked by us, so the price snapshot
        // and the stock level are the values this issue really happened at. The client never sends a price.
        var snapshot = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.UnitCost, p.QuantityOnHand })
            .FirstAsync(ct);

        var now = DateTime.UtcNow;
        var line = new WorkOrderPart
        {
            WorkOrderId = workOrderId,
            PartId = partId,
            Quantity = quantity,
            UnitPrice = snapshot.UnitCost,
            CreatedAt = now
        };
        _db.WorkOrderParts.Add(line);

        // BR-05 / BR-09: exactly one immutable Issue row, linked to the job, for the stock that just left.
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            PartId = partId,
            TransactionType = InventoryTransactionType.Issue,
            Quantity = -quantity,
            WorkOrderId = workOrderId,
            CreatedByUserId = technicianId,
            Notes = $"Used on work order #{workOrderId}",
            CreatedAt = now
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "WorkOrder {WorkOrderId}: issued {Quantity} of Part {PartId} at {UnitPrice} (line {LineId}) by {TechnicianId}; on hand now {OnHand}",
            workOrderId, quantity, partId, snapshot.UnitCost, line.Id, technicianId, snapshot.QuantityOnHand);

        return await _workOrders.GetByIdAsync(workOrderId, technicianId, requireOwnTechnician: true, ct);
    }

    public async Task<ServiceResult<WorkOrderDto>> ReturnAsync(
        int workOrderId, int lineId, string technicianId, byte[] rowVersion, CancellationToken ct)
    {
        if (await CheckJobAsync(workOrderId, technicianId, "returned", ct) is { } jobRefusal)
        {
            return jobRefusal;
        }

        var line = await _db.WorkOrderParts.AsNoTracking()
            .FirstOrDefaultAsync(wp => wp.Id == lineId && wp.WorkOrderId == workOrderId, ct);
        if (line is null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "That part is not on this job.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (!await _db.ClaimInProgressJobAsync(workOrderId, technicianId, rowVersion, ct))
        {
            await tx.RollbackAsync(ct);
            return await LostJobRaceAsync(workOrderId, technicianId, "returned", ct);
        }

        // Holding the job row, nobody else can be removing this line — but check it is still there anyway,
        // so a line can never be returned (and its stock re-added) twice.
        var removed = await _db.WorkOrderParts
            .Where(wp => wp.Id == lineId && wp.WorkOrderId == workOrderId)
            .ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, ChangedMessage);
        }

        // Stock only goes up here, so there is no floor to check. Inactive parts take their stock back too:
        // the unit physically returns to the store and the warehouse can write it off with an Adjustment.
        await _db.Parts
            .Where(p => p.Id == line.PartId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.QuantityOnHand, p => p.QuantityOnHand + line.Quantity), ct);

        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            PartId = line.PartId,
            TransactionType = InventoryTransactionType.Return,
            Quantity = line.Quantity,
            WorkOrderId = workOrderId,
            CreatedByUserId = technicianId,
            Notes = $"Returned from work order #{workOrderId}",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "WorkOrder {WorkOrderId}: returned line {LineId} ({Quantity} of Part {PartId}) by {TechnicianId}",
            workOrderId, lineId, line.Quantity, line.PartId, technicianId);

        return await _workOrders.GetByIdAsync(workOrderId, technicianId, requireOwnTechnician: true, ct);
    }

    // Ownership (BR-02: someone else's job is a 404), the central lock guard, then "in progress only" —
    // the same order and wording as the diagnosis edit, for a clear message before any write.
    private async Task<ServiceResult<WorkOrderDto>?> CheckJobAsync(
        int workOrderId, string technicianId, string verb, CancellationToken ct)
    {
        var job = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.Status, w.TechnicianId })
            .FirstOrDefaultAsync(ct);

        if (job is null || job.TechnicianId != technicianId)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        return JobRefusal(job.Status, verb) is { } refusal
            ? ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, refusal)
            : null;
    }

    private static string? JobRefusal(WorkOrderStatus status, string verb)
    {
        if (WorkOrderStateMachine.IsLocked(status))
        {
            return WorkOrderStateMachine.LockedMessage(status);
        }

        return WorkOrderStateMachine.AcceptsPartChanges(status)
            ? null
            : $"This job is {status}; parts can only be {verb} while it is in progress.";
    }

    private async Task<ServiceResult<WorkOrderDto>> LostJobRaceAsync(
        int workOrderId, string technicianId, string verb, CancellationToken ct)
    {
        var latest = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId && w.TechnicianId == technicianId)
            .Select(w => (WorkOrderStatus?)w.Status)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict, JobRefusal(latest.Value, verb) ?? ChangedMessage);
    }

    // AC-04: the conditional UPDATE matched nothing. Re-read to say why (deactivated meanwhile, or not enough).
    private async Task<ServiceResult<WorkOrderDto>> ShortageAsync(int partId, decimal quantity, CancellationToken ct)
    {
        var latest = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.Name, p.Unit, p.QuantityOnHand, p.IsActive })
            .FirstOrDefaultAsync(ct);

        if (latest is null || !latest.IsActive)
        {
            return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Validation, "Choose a part from the catalogue.");
        }

        return ServiceResult<WorkOrderDto>.Fail(ServiceErrorKind.Conflict,
            $"Not enough stock: only {latest.QuantityOnHand:0.###} {latest.Unit} of {latest.Name} on hand, " +
            $"so {quantity:0.###} cannot be used.");
    }
}
