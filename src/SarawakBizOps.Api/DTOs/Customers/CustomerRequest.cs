using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Customers;

// Shared shape for create and update — the MVP doesn't need them to diverge yet.
public class CustomerRequest
{
    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactPerson { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [EmailAddress, MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }
}
