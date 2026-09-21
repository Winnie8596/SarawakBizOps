using SarawakBizOps.Api.DTOs.Customers;

namespace SarawakBizOps.Api.Services.Customers;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct);
    Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct);
    Task<bool> UpdateAsync(int id, CustomerRequest request, CancellationToken ct);
}
