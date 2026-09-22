namespace SarawakBizOps.Api.Services.Common;

/// <summary>
/// Base64 &lt;-&gt; byte[] for the RowVersion concurrency token every mutating write now carries
/// (Phase 5's single conflict-token transport: a required body field, per Open Question 9).
/// </summary>
public static class RowVersionCodec
{
    public static bool TryParse(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
