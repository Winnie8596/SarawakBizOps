namespace SarawakBizOps.Api.DTOs.Parts;

/// <summary>
/// Optional list filter: <c>search</c> matches part number or name; inactive parts are hidden unless asked for.
/// The catalogue is paged on the server (<c>page</c> is 1-based) so a large one never arrives in a single response.
/// </summary>
public class PartFilter
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
}
