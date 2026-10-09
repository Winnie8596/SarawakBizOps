using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Parts;

public interface IPartService
{
    /// <summary>One page of the catalogue, by PartNumber, with the total count for the pager.</summary>
    Task<ServiceResult<PagedResult<PartDto>>> GetAllAsync(PartFilter filter, CancellationToken ct);
    Task<PartDto?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>AC-09 (API half): active parts whose QuantityOnHand is at or below their MinimumStockLevel.</summary>
    Task<IReadOnlyList<PartDto>> GetLowStockAsync(CancellationToken ct);

    /// <summary>Unique PartNumber. A new part starts with zero stock; stock arrives only through the ledger.</summary>
    Task<ServiceResult<PartDto>> CreateAsync(CreatePartRequest request, CancellationToken ct);

    /// <summary>Catalogue fields only — never QuantityOnHand (BR-09). RowVersion-guarded like every other edit.</summary>
    Task<ServiceResult<PartDto>> UpdateAsync(int id, UpdatePartRequest request, byte[] rowVersion, CancellationToken ct);
}
