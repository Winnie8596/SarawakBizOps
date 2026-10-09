namespace SarawakBizOps.Api.DTOs.Inventory;

/// <summary>
/// Optional history filters. <c>Type</c> is the enum name.
/// <para>
/// Dates come one of two ways. <c>FromDate</c>/<c>ToDate</c> are Malaysian calendar days (yyyy-MM-dd), both
/// inclusive; the server turns them into the UTC range, so no client has to know the offset. <c>From</c>/<c>To</c>
/// are exact instants (From inclusive, To exclusive) and must carry an offset (<c>Z</c> or <c>+08:00</c>): a bare
/// time is ambiguous and is refused rather than guessed at. One style per bound.
/// </para>
/// <para>The history is read newest first in pages of <c>Limit</c> rows; <c>Cursor</c> is the
/// <c>nextCursor</c> of the previous page.</para>
/// </summary>
public class InventoryTransactionFilter
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public int? PartId { get; set; }
    public string? Type { get; set; }
    public int? WorkOrderId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Limit { get; set; } = DefaultLimit;
    public string? Cursor { get; set; }
}
