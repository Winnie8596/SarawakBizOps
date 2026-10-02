using System.Text.Json;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>A model answer that passed <see cref="TechnicianAssistantOutput.Validate"/>: in shape, within the size
/// limits, and pointing only at past cases and parts that were in the context.</summary>
public record ValidatedTechnicianAnswer(
    IReadOnlyList<ValidatedCause> PossibleCauses,
    IReadOnlyList<string> SuggestedChecks,
    IReadOnlyList<ValidatedReference> RelevantPastCases,
    IReadOnlyList<ValidatedReference> RelevantParts);

public record ValidatedCause(string Cause, string Likelihood, string Reasoning);

public record ValidatedReference(int Id, string WhyRelevant);

/// <summary>
/// Checks the model's raw answer against the technician-assistant schema (pure, no I/O). The whole answer is refused
/// when it is not the expected JSON object, is oversized, or has no usable cause or check — the caller reports the
/// assistant as unavailable. Within an acceptable answer, individual bad items are dropped rather than failing the
/// lot: an unknown likelihood, an empty text, an id that was not offered (so a model cannot point the technician at a
/// job or part it was never shown), a duplicate id, or items beyond the per-list limit. Long texts are cut.
/// </summary>
public static class TechnicianAssistantOutput
{
    public const int MaxRawLength = 32_000;
    public const int MaxCauses = 5;
    public const int MaxChecks = 8;
    public const int MaxPastCases = 5;
    public const int MaxParts = 5;
    public const int MaxCauseLength = 200;
    public const int MaxReasoningLength = 500;
    public const int MaxCheckLength = 300;
    public const int MaxWhyLength = 300;

    private static readonly string[] Likelihoods = { "High", "Medium", "Low" };

    public static (ValidatedTechnicianAnswer? Answer, string? Error) Validate(
        string? raw, IReadOnlySet<int> offeredPastCaseIds, IReadOnlySet<int> offeredPartIds)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, "The answer was empty.");
        }

        if (raw.Length > MaxRawLength)
        {
            return (null, $"The answer was longer than {MaxRawLength} characters.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripCodeFence(raw));
        }
        catch (JsonException)
        {
            return (null, "The answer was not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "The answer was not a JSON object.");
            }

            if (!TryGetArray(root, "possibleCauses", out var causesJson)
                || !TryGetArray(root, "suggestedChecks", out var checksJson)
                || !TryGetArray(root, "relevantPastCases", out var pastCasesJson)
                || !TryGetArray(root, "relevantParts", out var partsJson))
            {
                return (null, "The answer is missing a required list.");
            }

            var causes = new List<ValidatedCause>();
            foreach (var item in causesJson.EnumerateArray())
            {
                if (causes.Count == MaxCauses) break;
                var cause = Text(item, "cause", MaxCauseLength);
                var likelihood = Likelihoods.FirstOrDefault(l =>
                    string.Equals(l, Text(item, "likelihood", 10), StringComparison.OrdinalIgnoreCase));
                if (cause is null || likelihood is null) continue;
                causes.Add(new ValidatedCause(cause, likelihood, Text(item, "reasoning", MaxReasoningLength) ?? ""));
            }

            var checks = new List<string>();
            foreach (var item in checksJson.EnumerateArray())
            {
                if (checks.Count == MaxChecks) break;
                if (item.ValueKind == JsonValueKind.String && Cut(item.GetString(), MaxCheckLength) is { } check)
                {
                    checks.Add(check);
                }
            }

            if (causes.Count == 0 || checks.Count == 0)
            {
                return (null, "The answer had no usable cause or check.");
            }

            return (new ValidatedTechnicianAnswer(
                causes,
                checks,
                References(pastCasesJson, "workOrderId", offeredPastCaseIds, MaxPastCases),
                References(partsJson, "partId", offeredPartIds, MaxParts)), null);
        }
    }

    private static List<ValidatedReference> References(JsonElement array, string idName, IReadOnlySet<int> offered, int max)
    {
        var references = new List<ValidatedReference>();
        foreach (var item in array.EnumerateArray())
        {
            if (references.Count == max) break;
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(idName, out var idJson)
                || idJson.ValueKind != JsonValueKind.Number
                || !idJson.TryGetInt32(out var id)
                || !offered.Contains(id)
                || references.Any(r => r.Id == id))
            {
                continue;
            }
            references.Add(new ValidatedReference(id, Text(item, "whyRelevant", MaxWhyLength) ?? ""));
        }
        return references;
    }

    private static bool TryGetArray(JsonElement root, string name, out JsonElement array)
        => root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array;

    private static string? Text(JsonElement item, string name, int max)
        => item.ValueKind == JsonValueKind.Object
           && item.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? Cut(value.GetString(), max)
            : null;

    private static string? Cut(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)].TrimEnd() + "…";
    }

    // A server in plain JSON mode sometimes wraps the object in a ```json fence; the object inside is what counts.
    private static string StripCodeFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var firstLineEnd = text.IndexOf('\n');
        var closing = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd > 0 && closing > firstLineEnd ? text[(firstLineEnd + 1)..closing] : text;
    }
}
