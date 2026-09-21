using SarawakBizOps.Api.DTOs.Equipment;

namespace SarawakBizOps.Api.Services.Equipment;

public interface IEquipmentService
{
    Task<IReadOnlyList<EquipmentDto>> GetAllAsync(int? customerId, CancellationToken ct);
    Task<EquipmentDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error, EquipmentDto? Equipment)> CreateAsync(EquipmentRequest request, CancellationToken ct);
}
