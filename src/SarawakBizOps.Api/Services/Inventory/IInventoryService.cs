using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Inventory;

/// <summary>
/// The inventory ledger (SDD §10). Deliberately has no update or delete: ledger rows are immutable
/// (BR-09) and a mistake is corrected with a new, offsetting Adjustment.
/// </summary>
public interface IInventoryService
{
    /// <summary>Newest first, one keyset page at a time (see <see cref="InventoryTransactionFilter"/>).</summary>
    Task<ServiceResult<CursorPage<InventoryTransactionDto>>> GetAllAsync(
        InventoryTransactionFilter filter, CancellationToken ct);

    /// <summary>
    /// Receive (+) or Adjustment (signed, mandatory reason). Writes exactly one ledger row and the matching
    /// QuantityOnHand change in one database transaction; stock never goes below zero.
    /// </summary>
    Task<ServiceResult<InventoryTransactionDto>> CreateAsync(
        CreateInventoryTransactionRequest request, string actingUserId, CancellationToken ct);
}
