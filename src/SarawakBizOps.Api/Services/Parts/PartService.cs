using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Parts;

public class PartService : IPartService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PartService> _logger;

    public PartService(ApplicationDbContext db, ILogger<PartService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ServiceResult<PagedResult<PartDto>>> GetAllAsync(PartFilter filter, CancellationToken ct)
    {
        if (filter.Page < 1)
        {
            return ServiceResult<PagedResult<PartDto>>.Fail(ServiceErrorKind.Validation, "Page must be 1 or more.");
        }

        if (filter.PageSize is < 1 or > PartFilter.MaxPageSize)
        {
            return ServiceResult<PagedResult<PartDto>>.Fail(ServiceErrorKind.Validation,
                $"PageSize must be between 1 and {PartFilter.MaxPageSize}.");
        }

        var query = _db.Parts.AsNoTracking();

        if (!filter.IncludeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(p => p.PartNumber.Contains(term) || p.Name.Contains(term));
        }

        var total = await query.CountAsync(ct);

        // PartNumber is unique, so the order is total and a part never shows up on two pages.
        var parts = await query
            .OrderBy(p => p.PartNumber)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return ServiceResult<PagedResult<PartDto>>.Ok(new PagedResult<PartDto>
        {
            Items = parts.Select(ToDto).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        });
    }

    public async Task<PartDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var part = await _db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return part is null ? null : ToDto(part);
    }

    public async Task<IReadOnlyList<PartDto>> GetLowStockAsync(CancellationToken ct)
    {
        // Most urgent first: the part furthest below its minimum leads the list.
        var parts = await _db.Parts.AsNoTracking()
            .Where(StockLevels.IsLow)
            .OrderBy(p => p.QuantityOnHand - p.MinimumStockLevel)
            .ThenBy(p => p.PartNumber)
            .ToListAsync(ct);

        return parts.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<PartDto>> CreateAsync(CreatePartRequest request, CancellationToken ct)
    {
        var invalid = Validate(request.PartNumber, request.Name, request.Unit, request.UnitCost, request.MinimumStockLevel);
        if (invalid is not null)
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        var partNumber = request.PartNumber.Trim();
        if (await PartNumberTakenAsync(partNumber, exceptId: null, ct))
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Conflict, DuplicateMessage(partNumber));
        }

        var part = new Part
        {
            PartNumber = partNumber,
            Name = request.Name.Trim(),
            Description = Blank(request.Description),
            Unit = request.Unit.Trim(),
            UnitCost = request.UnitCost,
            MinimumStockLevel = request.MinimumStockLevel,
            QuantityOnHand = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Parts.Add(part);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race with another create of the same number: the unique index is the final word.
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Conflict, DuplicateMessage(partNumber));
        }

        _logger.LogInformation("Part {PartId} ({PartNumber}) created", part.Id, part.PartNumber);
        return ServiceResult<PartDto>.Ok(ToDto(part));
    }

    public async Task<ServiceResult<PartDto>> UpdateAsync(
        int id, UpdatePartRequest request, byte[] rowVersion, CancellationToken ct)
    {
        if (!await _db.Parts.AnyAsync(p => p.Id == id, ct))
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.NotFound, "Part not found.");
        }

        var invalid = Validate(request.PartNumber, request.Name, request.Unit, request.UnitCost, request.MinimumStockLevel);
        if (invalid is not null)
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Validation, invalid);
        }

        var partNumber = request.PartNumber.Trim();
        if (await PartNumberTakenAsync(partNumber, exceptId: id, ct))
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Conflict, DuplicateMessage(partNumber));
        }

        var name = request.Name.Trim();
        var description = Blank(request.Description);
        var unit = request.Unit.Trim();

        int updated;
        try
        {
            // Same conflict-token rule as every other edit (Phase 5): only a row still carrying the
            // RowVersion the caller last saw is written. QuantityOnHand is not in the SET list at all —
            // stock only ever moves through the ledger (BR-09). A stock movement also bumps RowVersion,
            // so an edit made against a stale screen is refused rather than silently applied.
            updated = await _db.Parts
                .Where(p => p.Id == id && p.RowVersion == rowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.PartNumber, partNumber)
                    .SetProperty(p => p.Name, name)
                    .SetProperty(p => p.Description, description)
                    .SetProperty(p => p.Unit, unit)
                    .SetProperty(p => p.UnitCost, request.UnitCost)
                    .SetProperty(p => p.MinimumStockLevel, request.MinimumStockLevel)
                    .SetProperty(p => p.IsActive, request.IsActive), ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Conflict, DuplicateMessage(partNumber));
        }

        if (updated == 0)
        {
            return ServiceResult<PartDto>.Fail(ServiceErrorKind.Conflict,
                "This part was changed by someone else. Refresh and try again.");
        }

        _logger.LogInformation("Part {PartId} ({PartNumber}) updated; active: {IsActive}", id, partNumber, request.IsActive);
        return ServiceResult<PartDto>.Ok((await GetByIdAsync(id, ct))!);
    }

    private Task<bool> PartNumberTakenAsync(string partNumber, int? exceptId, CancellationToken ct)
        => _db.Parts.AnyAsync(p => p.PartNumber == partNumber && (exceptId == null || p.Id != exceptId), ct);

    private static string? Validate(string partNumber, string name, string unit, decimal unitCost, decimal minimum)
    {
        if (string.IsNullOrWhiteSpace(partNumber)) return "Part number is required.";
        if (string.IsNullOrWhiteSpace(name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(unit)) return "Unit is required.";
        if (unitCost < 0) return "Unit cost cannot be negative.";
        if (unitCost > Quantities.Max) return "Unit cost is too large.";
        if (!Quantities.HasAtMostDecimals(unitCost, 2)) return "Unit cost can have at most 2 decimal places (sen).";
        if (minimum < 0) return "Minimum stock level cannot be negative.";
        if (minimum > Quantities.Max) return "Minimum stock level is too large.";
        if (!Quantities.HasAtMostDecimals(minimum, Quantities.Scale))
            return $"Minimum stock level can have at most {Quantities.Scale} decimal places.";
        return null;
    }

    private static string DuplicateMessage(string partNumber) => $"Part number {partNumber} is already in use.";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // 2601: duplicate key in a unique index; 2627: unique constraint violation.
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }
        return false;
    }

    public static PartDto ToDto(Part p) => new()
    {
        Id = p.Id,
        PartNumber = p.PartNumber,
        Name = p.Name,
        Description = p.Description,
        Unit = p.Unit,
        UnitCost = p.UnitCost,
        QuantityOnHand = p.QuantityOnHand,
        MinimumStockLevel = p.MinimumStockLevel,
        IsActive = p.IsActive,
        IsLowStock = StockLevels.IsLowStock(p),
        CreatedAt = p.CreatedAt,
        RowVersion = p.RowVersion is null ? string.Empty : Convert.ToBase64String(p.RowVersion)
    };
}
