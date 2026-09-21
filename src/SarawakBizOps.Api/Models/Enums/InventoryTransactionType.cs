namespace SarawakBizOps.Api.Models.Enums;

// Section 10 (Inventory Accounting Rule) of the design doc.
public enum InventoryTransactionType
{
    Receive,
    Issue,
    Return,
    Adjustment
}
