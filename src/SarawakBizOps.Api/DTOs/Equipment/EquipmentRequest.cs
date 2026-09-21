using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Equipment;

public class EquipmentRequest
{
    [Required]
    public int CustomerId { get; set; }

    [Required, MaxLength(100)]
    public string SerialNumber { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string EquipmentType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Brand { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    public DateTime? InstallationDate { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }
}
