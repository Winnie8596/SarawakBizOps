using SarawakBizOps.Api.DTOs.History;

namespace SarawakBizOps.Api.Services.History;

public interface IServiceHistoryService
{
    /// <returns>null when the customer does not exist.</returns>
    Task<ServiceHistoryDto?> ForCustomerAsync(int customerId, CancellationToken ct);

    /// <returns>null when the equipment does not exist.</returns>
    Task<ServiceHistoryDto?> ForEquipmentAsync(int equipmentId, CancellationToken ct);
}
