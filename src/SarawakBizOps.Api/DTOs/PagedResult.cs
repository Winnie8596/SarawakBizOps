namespace SarawakBizOps.Api.DTOs;

/// <summary>One page of a list that is browsed by page number (the parts catalogue).</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    /// <summary>Rows matching the filters across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>1-based.</summary>
    public int Page { get; set; }

    public int PageSize { get; set; }
}

/// <summary>
/// One page of a list that is read forwards with keyset paging (the stock ledger). <see cref="NextCursor"/> is
/// opaque: pass it back as <c>cursor</c> for the next page; null means this was the last one.
/// </summary>
public class CursorPage<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public string? NextCursor { get; set; }
}
