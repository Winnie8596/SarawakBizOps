using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.DTOs.Equipment;

/// <summary>
/// Update shape for PUT /api/equipment/{id}. There is deliberately no CustomerId:
/// equipment cannot move to another customer, because service requests rely on
/// the Equipment↔Customer pairing staying stable (BR-10).
/// </summary>
public class EquipmentUpdateRequest
{
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

    /// <summary>One of Active, Inactive, UnderMaintenance, Retired.</summary>
    [Required]
    public string Status { get; set; } = string.Empty;
}
