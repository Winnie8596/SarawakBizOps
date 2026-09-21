namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.6 — the join entity between WorkOrder and Part.
// Carries its own data (Quantity, a UnitPrice snapshot), which is what
// makes it a real entity rather than a plain many-to-many link table.
public class WorkOrderPart
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public int PartId { get; set; }
    public Part? Part { get; set; }

    public decimal Quantity { get; set; }

    // Snapshot of Part.UnitCost at the moment of use, so a later price
    // change doesn't rewrite history.
    public decimal UnitPrice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
