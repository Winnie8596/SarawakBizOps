using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Inventory;

public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ApplicationDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<InventoryTransactionDto>>> GetAllAsync(
        InventoryTransactionFilter filter, CancellationToken ct)
    {
        var query = _db.InventoryTransactions.AsNoTracking()
            .Include(t => t.Part)
            .Include(t => t.CreatedByUser)
            .AsQueryable();

        if (filter.PartId is { } partId)
        {
            query = query.Where(t => t.PartId == partId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            if (!TryParseName<InventoryTransactionType>(filter.Type, out var type))
            {
                return ServiceResult<IReadOnlyList<InventoryTransactionDto>>.Fail(
                    ServiceErrorKind.Validation, NameListMessage<InventoryTransactionType>("Type"));
            }
            query = query.Where(t => t.TransactionType == type);
        }

        if (filter.WorkOrderId is { } workOrderId)
        {
            query = query.Where(t => t.WorkOrderId == workOrderId);
        }

        if (filter.From is { } from && filter.To is { } to && from >= to)
        {
            return ServiceResult<IReadOnlyList<InventoryTransactionDto>>.Fail(
                ServiceErrorKind.Validation, "The 'from' date must be before the 'to' date.");
        }

        if (filter.From is { } fromUtc)
        {
            var bound = AsUtc(fromUtc);
            query = query.Where(t => t.CreatedAt >= bound);
        }

        if (filter.To is { } toUtc)
        {
            var bound = AsUtc(toUtc);
            query = query.Where(t => t.CreatedAt < bound);
        }

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<InventoryTransactionDto>>.Ok(rows.Select(r => ToDto(r)).ToList());
    }

    public async Task<ServiceResult<InventoryTransactionDto>> CreateAsync(
        CreateInventoryTransactionRequest request, string actingUserId, CancellationToken ct)
    {
        if (!TryParseName<InventoryTransactionType>(request.TransactionType, out var type))
        {
            return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.Validation,
                "TransactionType must be Receive or Adjustment.");
        }

        var invalid = StockMovementRules.Validate(type, request.Quantity, request.Notes);
        if (invalid is not null)
        {
            return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        var part = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.IsActive })
            .FirstOrDefaultAsync(ct);
        if (part is null)
        {
            return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.NotFound, "Part not found.");
        }

        // A deactivated part is out of the catalogue: nothing new is received into it. An Adjustment is
        // still allowed, so leftover stock of a discontinued part can be written off with a reason.
        var requireActive = type == InventoryTransactionType.Receive;
        if (requireActive && !part.IsActive)
        {
            return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.Conflict,
                "This part is inactive; reactivate it before receiving stock.");
        }

        var quantity = request.Quantity;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // One atomic, conditional UPDATE is both the stock change and the concurrency guard: the balance
        // is computed by the database from its current value (never read-modify-written from here), and
        // a movement that would take it below zero matches no row. Two simultaneous write-offs of the last
        // units therefore cannot both succeed, without a retry loop.
        var changed = await _db.Parts
            .Where(p => p.Id == request.PartId
                        && p.QuantityOnHand + quantity >= 0
                        && (!requireActive || p.IsActive))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.QuantityOnHand, p => p.QuantityOnHand + quantity), ct);

        if (changed == 0)
        {
            await tx.RollbackAsync(ct);
            return await RefusalAsync(request.PartId, quantity, ct);
        }

        // BR-09: exactly one immutable ledger row per stock change, committed together with it.
        var entry = new InventoryTransaction
        {
            PartId = request.PartId,
            TransactionType = type,
            Quantity = quantity,
            CreatedByUserId = actingUserId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.InventoryTransactions.Add(entry);
        await _db.SaveChangesAsync(ct);

        var onHand = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == request.PartId)
            .Select(p => p.QuantityOnHand)
            .FirstAsync(ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "InventoryTransaction {TransactionId}: {Type} {Quantity} of Part {PartId} by {ActingUserId}; on hand now {OnHand}",
            entry.Id, type, quantity, request.PartId, actingUserId, onHand);

        var saved = await _db.InventoryTransactions.AsNoTracking()
            .Include(t => t.Part)
            .Include(t => t.CreatedByUser)
            .FirstAsync(t => t.Id == entry.Id, ct);
        return ServiceResult<InventoryTransactionDto>.Ok(ToDto(saved, onHand));
    }

    // The conditional UPDATE matched nothing: re-read to say why (removed, deactivated meanwhile, or not enough stock).
    private async Task<ServiceResult<InventoryTransactionDto>> RefusalAsync(int partId, decimal quantity, CancellationToken ct)
    {
        var latest = await _db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.QuantityOnHand, p.Unit, p.IsActive })
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.NotFound, "Part not found.");
        }

        if (!StockMovementRules.KeepsStockNonNegative(latest.QuantityOnHand, quantity))
        {
            return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.Conflict,
                $"Not enough stock: only {latest.QuantityOnHand:0.###} {latest.Unit} on hand, " +
                $"so it cannot be reduced by {-quantity:0.###}.");
        }

        return ServiceResult<InventoryTransactionDto>.Fail(ServiceErrorKind.Conflict,
            "This part is inactive; reactivate it before receiving stock.");
    }

    // Query-string DateTimes arrive as Unspecified (no offset) or Local (with one); the column holds UTC.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool TryParseName<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out result);
    }

    private static string NameListMessage<TEnum>(string field) where TEnum : struct, Enum
        => $"{field} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";

    private static InventoryTransactionDto ToDto(InventoryTransaction t, decimal? onHandAfter = null) => new()
    {
        Id = t.Id,
        PartId = t.PartId,
        PartNumber = t.Part?.PartNumber ?? string.Empty,
        PartName = t.Part?.Name ?? string.Empty,
        Unit = t.Part?.Unit ?? string.Empty,
        TransactionType = t.TransactionType.ToString(),
        Quantity = t.Quantity,
        QuantityOnHandAfter = onHandAfter,
        WorkOrderId = t.WorkOrderId,
        CreatedByName = t.CreatedByUser?.FullName ?? string.Empty,
        Notes = t.Notes,
        CreatedAt = t.CreatedAt
    };
}
