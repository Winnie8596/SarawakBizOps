using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Equipment;

public class EquipmentService : IEquipmentService
{
    private readonly ApplicationDbContext _db;

    public EquipmentService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<EquipmentDto>> GetAllAsync(int? customerId, CancellationToken ct)
    {
        var query = _db.Equipment.AsNoTracking().AsQueryable();
        if (customerId.HasValue)
        {
            query = query.Where(e => e.CustomerId == customerId.Value);
        }

        var items = await query.OrderBy(e => e.SerialNumber).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<EquipmentDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var equipment = await _db.Equipment.FindAsync(new object[] { id }, ct);
        return equipment is null ? null : ToDto(equipment);
    }

    public async Task<(bool Success, string? Error, EquipmentDto? Equipment)> CreateAsync(
        EquipmentRequest request, CancellationToken ct)
    {
        // BR-10 groundwork: equipment must belong to a real customer. The
        // ServiceRequest layer will later re-check this pairing on its own.
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
        {
            return (false, "The selected customer does not exist.", null);
        }

        var serialTaken = await _db.Equipment.AnyAsync(e => e.SerialNumber == request.SerialNumber, ct);
        if (serialTaken)
        {
            return (false, "An equipment record with this serial number already exists.", null);
        }

        var equipment = new Models.Entities.Equipment
        {
            CustomerId = request.CustomerId,
            SerialNumber = request.SerialNumber,
            EquipmentType = request.EquipmentType,
            Brand = request.Brand,
            Model = request.Model,
            InstallationDate = request.InstallationDate,
            Location = request.Location,
            Status = EquipmentStatus.Active
        };

        _db.Equipment.Add(equipment);
        await _db.SaveChangesAsync(ct);

        return (true, null, ToDto(equipment));
    }

    public async Task<ServiceResult<EquipmentDto>> UpdateAsync(
        int id, EquipmentUpdateRequest request, CancellationToken ct)
    {
        var equipment = await _db.Equipment.FindAsync(new object[] { id }, ct);
        if (equipment is null)
        {
            return ServiceResult<EquipmentDto>.Fail(ServiceErrorKind.NotFound, "Equipment not found.");
        }

        if (!Enum.TryParse<EquipmentStatus>(request.Status, ignoreCase: false, out var status)
            || !Enum.IsDefined(status))
        {
            return ServiceResult<EquipmentDto>.Fail(ServiceErrorKind.Validation,
                $"Status must be one of: {string.Join(", ", Enum.GetNames<EquipmentStatus>())}.");
        }

        var serialTaken = await _db.Equipment.AnyAsync(
            e => e.SerialNumber == request.SerialNumber && e.Id != id, ct);
        if (serialTaken)
        {
            return ServiceResult<EquipmentDto>.Fail(ServiceErrorKind.Validation,
                "An equipment record with this serial number already exists.");
        }

        // CustomerId is intentionally not updatable (see EquipmentUpdateRequest).
        equipment.SerialNumber = request.SerialNumber;
        equipment.EquipmentType = request.EquipmentType;
        equipment.Brand = request.Brand;
        equipment.Model = request.Model;
        equipment.InstallationDate = request.InstallationDate;
        equipment.Location = request.Location;
        equipment.Status = status;

        await _db.SaveChangesAsync(ct);
        return ServiceResult<EquipmentDto>.Ok(ToDto(equipment));
    }

    private static EquipmentDto ToDto(Models.Entities.Equipment e) => new()
    {
        Id = e.Id,
        CustomerId = e.CustomerId,
        SerialNumber = e.SerialNumber,
        EquipmentType = e.EquipmentType,
        Brand = e.Brand,
        Model = e.Model,
        InstallationDate = e.InstallationDate,
        Status = e.Status.ToString(),
        Location = e.Location
    };
}
