namespace SarawakBizOps.Api.DTOs.Parts;

/// <summary>Optional list filter: <c>search</c> matches part number or name; inactive parts are hidden unless asked for.</summary>
public class PartFilter
{
    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
}
