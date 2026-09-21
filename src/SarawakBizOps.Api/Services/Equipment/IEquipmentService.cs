using SarawakBizOps.Api.DTOs.Equipment;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Equipment;

public interface IEquipmentService
{
    Task<IReadOnlyList<EquipmentDto>> GetAllAsync(int? customerId, CancellationToken ct);
    Task<EquipmentDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error, EquipmentDto? Equipment)> CreateAsync(EquipmentRequest request, CancellationToken ct);
    Task<ServiceResult<EquipmentDto>> UpdateAsync(int id, EquipmentUpdateRequest request, CancellationToken ct);
}
