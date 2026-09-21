using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Customers;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Services.Customers;

public class CustomerService : ICustomerService
{
    private readonly ApplicationDbContext _db;

    public CustomerService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct)
    {
        // Materialize first, then project — calling a mapping method inside
        // .Select() on an IQueryable would fail EF Core's SQL translation.
        var customers = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.CompanyName)
            .ToListAsync(ct);

        return customers.Select(ToDto).ToList();
    }

    public async Task<CustomerDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var customer = await _db.Customers.FindAsync(new object[] { id }, ct);
        return customer is null ? null : ToDto(customer);
    }

    public async Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct)
    {
        var customer = new Customer
        {
            CompanyName = request.CompanyName,
            ContactPerson = request.ContactPerson,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
            CreatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        return ToDto(customer);
    }

    public async Task<bool> UpdateAsync(int id, CustomerRequest request, CancellationToken ct)
    {
        var customer = await _db.Customers.FindAsync(new object[] { id }, ct);
        if (customer is null)
        {
            return false;
        }

        customer.CompanyName = request.CompanyName;
        customer.ContactPerson = request.ContactPerson;
        customer.Phone = request.Phone;
        customer.Email = request.Email;
        customer.Address = request.Address;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static CustomerDto ToDto(Customer c) => new()
    {
        Id = c.Id,
        CompanyName = c.CompanyName,
        ContactPerson = c.ContactPerson,
        Phone = c.Phone,
        Email = c.Email,
        Address = c.Address,
        CreatedAt = c.CreatedAt
    };
}
