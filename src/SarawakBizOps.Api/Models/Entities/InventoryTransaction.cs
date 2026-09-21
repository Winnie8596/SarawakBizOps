using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Models.Entities;

// Design doc Section 6.7 / Section 10. Quantity is signed: positive
// increases stock, negative decreases it. Rows are immutable (BR-09) —
// corrections are made with a new offsetting Adjustment, never an edit.
public class InventoryTransaction
{
    public int Id { get; set; }

    public int PartId { get; set; }
    public Part? Part { get; set; }

    public InventoryTransactionType TransactionType { get; set; }
    public decimal Quantity { get; set; }

    // Set only for Issue/Return transactions tied to a specific job.
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
