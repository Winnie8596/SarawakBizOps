namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>One part line on a job. <see cref="Id"/> is the line id used to return it.</summary>
public class WorkOrderPartDto
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }

    /// <summary>The catalogue price when the part was added — later price changes do not rewrite it.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Quantity × UnitPrice, rounded to sen.</summary>
    public decimal LineTotal { get; set; }

    public DateTime CreatedAt { get; set; }
}
