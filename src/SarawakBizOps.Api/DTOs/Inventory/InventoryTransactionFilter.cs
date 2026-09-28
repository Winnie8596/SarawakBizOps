namespace SarawakBizOps.Api.DTOs.Inventory;

/// <summary>
/// Optional history filters. <c>Type</c> is the enum name. <c>From</c> is inclusive and <c>To</c> exclusive,
/// both UTC instants — the web app turns a MYT calendar day into the matching pair.
/// </summary>
public class InventoryTransactionFilter
{
    public int? PartId { get; set; }
    public string? Type { get; set; }
    public int? WorkOrderId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
