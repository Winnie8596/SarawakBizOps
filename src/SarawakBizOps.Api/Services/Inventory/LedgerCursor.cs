using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace SarawakBizOps.Api.Services.Inventory;

/// <summary>
/// The position of the last ledger row a client has seen, for keyset paging on (CreatedAt DESC, Id DESC).
/// The next page is every row strictly after it in that order, so a page boundary never skips or repeats a row,
/// even when several movements share a timestamp or new ones are recorded while someone is reading.
/// On the wire it is opaque: base64url of "{CreatedAt ticks}:{Id}".
/// </summary>
public readonly record struct LedgerCursor(DateTime CreatedAt, int Id)
{
    public string Encode()
        => Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            $"{CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{Id.ToString(CultureInfo.InvariantCulture)}"));

    public static bool TryDecode(string value, out LedgerCursor cursor)
    {
        cursor = default;
        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(bytes).Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        cursor = new LedgerCursor(new DateTime(ticks, DateTimeKind.Utc), id);
        return true;
    }
}
