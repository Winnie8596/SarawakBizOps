using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.2.
public class Equipment
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string SerialNumber { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public DateTime? InstallationDate { get; set; }
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Active;
    public string? Location { get; set; }

    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();
}
