namespace SarawakBizOps.Api.DTOs.WorkOrders;

/// <summary>Optional list filter for the office work-orders view; status is the enum name.</summary>
public class WorkOrderFilter
{
    public string? Status { get; set; }
}
