namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// One spelling for brands, models and codes on both sides of every comparison (docs/ai/decisions.md G3, G6, G7):
/// upper case, with spaces and dashes removed, so <c>kb-10t</c>, <c>KB 10T</c> and <c>KB‑10T</c> (a non-breaking
/// hyphen) are all <c>KB10T</c>. Equipment's <c>Brand</c> and <c>Model</c> are free text typed by staff, so the
/// knowledge base cannot rely on them being written the way a manual writes them.
/// </summary>
public static class IdentifierNormaliser
{
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var result = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || IsDash(c)) continue;
            result.Append(char.ToUpperInvariant(c));
        }
        return result.ToString();
    }

    // ASCII hyphen-minus plus the Unicode dash punctuation a pasted manual or a phone keyboard may produce.
    private static bool IsDash(char c)
        => c == '-' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.DashPunctuation;
}
