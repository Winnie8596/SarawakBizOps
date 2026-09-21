using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Users;

public class ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}
