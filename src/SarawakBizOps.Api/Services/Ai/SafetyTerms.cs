using System.Text.RegularExpressions;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The deterministic safety backstop for AI-01's suggested checks (docs/ai/decisions.md G13, G19, G20): a check is a
/// safety step if the model flags it <b>or</b> its text matches here, so a model that forgets the flag still cannot
/// show an uncited isolation or pressure step. Pure and static by design — not configuration, so no deployment
/// setting can switch the backstop off.
/// <para>
/// Terms match case-insensitively on whole words and phrases, with punctuation treated as a space, so
/// "De-energise" and "de energise" both match while "tekan" never matches "tekanan". Value patterns (a voltage, a
/// pressure, a refrigerant code) match the original text. False positives are acceptable; false negatives are not —
/// a word is narrowed to phrases only where it misfires in everyday repair talk (bare "kunci" is also a spanner).
/// </para>
/// </summary>
public static partial class SafetyTerms
{
    /// <summary>English, v1.2 §6.4 plus G19.</summary>
    public static readonly IReadOnlyList<string> English =
    [
        "isolate", "isolation", "lockout", "lock out", "tagout", "de-energise", "de-energize", "live", "voltage",
        "high voltage", "electrical panel", "busbar", "capacitor", "discharge", "pressure", "pressurised",
        "pressurized", "refrigerant", "gas line", "fuel", "hot surface", "rotating", "remove guard", "bypass", "earth",
        "ground fault",
        "steam", "hydraulic", "blade", "moving parts"
    ];

    /// <summary>Bahasa Malaysia, v1.2 §6.4 plus G19. "asingkan", "kunci" and "arus" appear only in phrases.</summary>
    public static readonly IReadOnlyList<string> Malay =
    [
        "voltan", "elektrik", "bekalan elektrik", "putuskan bekalan", "tekanan", "bahan penyejuk", "gas", "bahan api",
        "permukaan panas",
        "asingkan bekalan", "asingkan kuasa", "diasingkan", "mengasingkan", "pengasingan", "suis pengasing",
        "kunci suis", "kunci dan tag", "arus elektrik", "arus tinggi", "arus bocor", "litar", "litar pintas",
        "kapasitor", "nyahcas", "buang cas", "lepas cas", "berputar", "wap", "wap panas", "hidraulik", "bilah",
        "renjatan", "potong bekalan", "suis utama", "tiga fasa"
    ];

    /// <summary>Loanwords technicians use in either language (G19).</summary>
    public static readonly IReadOnlyList<string> Loanwords =
    [
        "LOTO", "ampere", "amp", "freon", "MCB", "DB", "breaker"
    ];

    private static readonly string[] Phrases = English.Concat(Malay).Concat(Loanwords)
        .Select(t => " " + Normalize(t) + " ")
        .Distinct()
        .ToArray();

    private static readonly Regex[] ValuePatterns = { VoltagePattern(), PressurePattern(), RefrigerantPattern() };

    /// <summary>Whether <paramref name="text"/> names a hazard: any term or value pattern.</summary>
    public static bool Matches(string? text) => FirstMatch(text) is not null;

    /// <summary>The first term (normalised) or value that matched, for logs and tests; <c>null</c> when none did.</summary>
    public static string? FirstMatch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var padded = " " + Normalize(text) + " ";
        foreach (var phrase in Phrases)
        {
            if (padded.Contains(phrase, StringComparison.Ordinal)) return phrase.Trim();
        }

        foreach (var pattern in ValuePatterns)
        {
            var match = pattern.Match(text);
            if (match.Success) return match.Value;
        }

        return null;
    }

    /// <summary>Lower case, anything but a letter or digit as one space: "De-energise!" → "de energise".</summary>
    private static string Normalize(string text)
        => NonWord().Replace(text.ToLowerInvariant(), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    // 415V, 11 kV, 240 VAC, 12.5V.
    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s?(?:k?v|vac|vdc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoltagePattern();

    // 150 psi, 8 bar, 600 kPa, 2.5 MPa.
    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s?(?:psig?|bar|kpa|mpa)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PressurePattern();

    // R410A, R-22, R134a, R32.
    [GeneratedRegex(@"\bR-?\d{2,3}[A-Za-z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefrigerantPattern();
}
