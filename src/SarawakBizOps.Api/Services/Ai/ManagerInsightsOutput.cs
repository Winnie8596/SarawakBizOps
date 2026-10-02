using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// Checks the model's summary of a Manager question (pure, no I/O) — the enforcement half of AC-10. Besides the
/// shape and size, <b>every number in the summary must be one of the numbers the model was given</b> (or that
/// number rounded to fewer decimals, as people write "47 hours" for 47.03): a model that adds, subtracts, converts
/// or invents a figure has its summary refused, and the Manager sees the backend's figures without it.
/// </summary>
public static partial class ManagerInsightsOutput
{
    public const int MaxRawLength = 4_000;
    public const int MaxSummaryLength = 800;

    public static (string? Summary, string? Error) Validate(string? raw, string factsJson)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, "The answer was empty.");
        }

        if (raw.Length > MaxRawLength)
        {
            return (null, $"The answer was longer than {MaxRawLength} characters.");
        }

        string? summary;
        try
        {
            using var document = JsonDocument.Parse(TechnicianAssistantOutput.StripCodeFence(raw));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("summary", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return (null, "The answer has no summary.");
            }
            summary = value.GetString()?.Trim();
        }
        catch (JsonException)
        {
            return (null, "The answer was not valid JSON.");
        }

        if (string.IsNullOrEmpty(summary))
        {
            return (null, "The summary was empty.");
        }

        if (summary.Length > MaxSummaryLength)
        {
            return (null, $"The summary was longer than {MaxSummaryLength} characters.");
        }

        var given = NumbersIn(factsJson, FactNumber());
        var stray = NumbersIn(summary, SummaryNumber()).FirstOrDefault(n => !IsGiven(n, given));
        if (stray is not null)
        {
            // Our wording and the offending number (which is not business data the caller lacks), never the summary.
            return (null, $"The summary contains a number that was not in the figures ({stray}).");
        }

        return (summary, null);
    }

    private static bool IsGiven(decimal? number, IReadOnlyList<decimal?> given)
        => given.Any(g => g == number
            || Math.Round(g!.Value, 0, MidpointRounding.AwayFromZero) == number
            || Math.Round(g.Value, 1, MidpointRounding.AwayFromZero) == number
            || Math.Round(g.Value, 2, MidpointRounding.AwayFromZero) == number);

    private static List<decimal?> NumbersIn(string text, Regex pattern)
        => pattern.Matches(text)
            .Select(m => decimal.TryParse(m.Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                ? d : (decimal?)null)
            .Where(d => d is not null)
            .ToList();

    // In the facts JSON: plain numbers (values) and numbers inside labels ("2026", "#12", "minimum 5").
    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex FactNumber();

    // In the summary, also with thousands separators ("1,234.50"); a sentence's full stop is not a decimal point.
    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?")]
    private static partial Regex SummaryNumber();
}
