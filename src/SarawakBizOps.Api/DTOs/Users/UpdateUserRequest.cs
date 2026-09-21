using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Users;

/// <summary>Partial update: a null property means "leave unchanged".</summary>
public class UpdateUserRequest
{
    [MinLength(1), MaxLength(150)]
    public string? FullName { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }
}
