using System.ComponentModel.DataAnnotations;

namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.5. Quantities are decimal, not int, because some
// consumables are measured in litres/metres/kg rather than whole units.
public class Part
{
    public int Id { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal UnitCost { get; set; }

    // Cached running balance — updated only by inventory service logic,
    // never edited directly (BR-09, design doc Section 10).
    public decimal QuantityOnHand { get; set; }
    public decimal MinimumStockLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optimistic concurrency for catalogue edits: an update must carry the RowVersion the caller last saw
    // (PartService). Stock movements do not use it: they are a conditional UPDATE of QuantityOnHand (backed by
    // CK_Parts_QuantityOnHand_NonNegative), which also bumps RowVersion, so a catalogue edit made from a
    // screen loaded before a movement gets a 409 and is re-read.
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<WorkOrderPart> WorkOrderParts { get; set; } = new List<WorkOrderPart>();
    public ICollection<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
}
