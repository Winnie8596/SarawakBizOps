namespace SarawakBizOps.Api.DTOs.ServiceRequests;

/// <summary>Optional list filters; status and priority are the enum names.</summary>
public class ServiceRequestFilter
{
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public int? CustomerId { get; set; }
}
