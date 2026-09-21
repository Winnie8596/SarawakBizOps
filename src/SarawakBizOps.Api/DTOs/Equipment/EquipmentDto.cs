namespace SarawakBizOps.Api.DTOs.Equipment;

public class EquipmentDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public DateTime? InstallationDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Location { get; set; }
}
