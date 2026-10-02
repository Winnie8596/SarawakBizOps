using System.Text.RegularExpressions;
using SarawakBizOps.Api.Services.Analytics;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// Maps a Manager's question to one allow-listed topic and a period (Phase 14, AI-02) — rules only, pure, so the
/// same question always maps the same way and every mapping is unit-tested. The model never sees the question: it
/// is only ever asked to word the figures the chosen analytics operation returned.
/// <para>
/// Each topic scores the weights of its phrases found in the question; the highest score wins. No match, or a tie
/// between two topics, is a refusal. So is a question the operations cannot answer truthfully even when a topic
/// matches — a breakdown by technician or customer, a forecast, money the system does not record, or a period
/// shorter than a month — rather than answering a different question than the one asked.
/// </para>
/// </summary>
public static partial class ManagerInsightsIntentMapper
{
    public const string UnsupportedMessage =
        "I can only answer questions about the figures below. Try one of the example questions.";

    public const string BreakdownMessage =
        "I can't break the figures down by technician, customer or person — only for the business as a whole.";

    public const string ForecastMessage = "I only report what has already happened; I can't forecast or predict.";

    public const string MoneyMessage = "Revenue, profit and pay are not recorded in this system's analytics.";

    public const string GranularityMessage =
        "The figures are kept by calendar month. Ask about this month, last month or the last few months.";

    public static readonly string RangeMessage = $"I can look back between 1 and {AnalyticsRules.MaxMonths} months.";

    private static readonly (Regex Pattern, string Message)[] Refusals =
    {
        (BreakdownPattern(), BreakdownMessage),
        (ForecastPattern(), ForecastMessage),
        (MoneyPattern(), MoneyMessage),
        (ShortPeriodPattern(), GranularityMessage)
    };

    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["eighteen"] = 18,
        ["twenty four"] = 24
    };

    public static InsightMapping Map(string question, DateTime nowUtc)
    {
        var text = Normalize(question);

        // First, so a question no operation can answer truthfully is refused for what it is, matched topic or not.
        foreach (var (pattern, message) in Refusals)
        {
            if (pattern.IsMatch(text)) return InsightMapping.Refuse(message);
        }

        var scores = ManagerInsightTopics.All
            .Select(topic => (Topic: topic, Score: topic.Phrases.Where(p => Contains(text, p.Text)).Sum(p => p.Weight)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ToList();

        if (scores.Count == 0)
        {
            return InsightMapping.Refuse(UnsupportedMessage);
        }

        if (scores.Count > 1 && scores[0].Score == scores[1].Score)
        {
            return InsightMapping.Refuse(
                $"That question could be about “{scores[0].Topic.Title}” or “{scores[1].Topic.Title}”. Ask about one at a time.");
        }

        var period = ParsePeriod(text, nowUtc);
        if (period is null)
        {
            return InsightMapping.Refuse(RangeMessage);
        }

        return InsightMapping.Answer(scores[0].Topic, period);
    }

    /// <summary>
    /// "this month" → the current month; "last month" → the previous one (the window then runs from it to now);
    /// "last 3 months", "past quarter", "last year", "this year" → that many months ending with the current one;
    /// nothing said → the dashboard's six months. Null when the number of months is out of range.
    /// </summary>
    public static InsightPeriod? ParsePeriod(string normalizedText, DateTime nowUtc)
    {
        var count = MonthCountPattern().Match(normalizedText);
        if (count.Success)
        {
            var value = count.Groups["n"].Value;
            var months = int.TryParse(value, out var n) ? n : NumberWords[value];
            return months is < 1 or > AnalyticsRules.MaxMonths ? null : InsightPeriod.Months(months);
        }

        if (CurrentMonthPattern().IsMatch(normalizedText)) return InsightPeriod.CurrentMonth;
        if (PreviousMonthPattern().IsMatch(normalizedText)) return InsightPeriod.PreviousMonth;
        if (QuarterPattern().IsMatch(normalizedText)) return InsightPeriod.Months(3);
        if (HalfYearPattern().IsMatch(normalizedText)) return InsightPeriod.Months(6);
        if (YearPattern().IsMatch(normalizedText)) return InsightPeriod.Months(12);
        if (ThisYearPattern().IsMatch(normalizedText)) return InsightPeriod.Months(ReportRules.ToMyt(nowUtc).Month);

        return InsightPeriod.Default;
    }

    /// <summary>Lower case, punctuation as spaces, single spaces: "What's the  status?" → "what s the status".</summary>
    public static string Normalize(string question)
        => Spaces().Replace(NonWord().Replace(question.ToLowerInvariant(), " "), " ").Trim();

    // At the start of a word: "complet" matches "completed" but not "incomplete".
    private static bool Contains(string text, string phrase)
    {
        for (var at = text.IndexOf(phrase, StringComparison.Ordinal); at >= 0; at = text.IndexOf(phrase, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || text[at - 1] == ' ') return true;
        }
        return false;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\b(by|per|each|which|for|of|every) (technician|customer|staff|employee|person|client)s?\b|\bwho\b")]
    private static partial Regex BreakdownPattern();

    [GeneratedRegex(@"\b(forecast|predict|projection)|\bnext (week|month|quarter|year)\b|\bwill (be|we|there)\b")]
    private static partial Regex ForecastPattern();

    [GeneratedRegex(@"\b(revenue|profit|income|sales|invoice|salar|wage|payroll|margin)")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"\btoday\b|\byesterday\b|\b(this|last|past|previous) (week|day|fortnight)\b|\bper (day|week)\b|\bdaily\b|\bweekly\b")]
    private static partial Regex ShortPeriodPattern();

    [GeneratedRegex(@"\b(?<n>\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|eighteen|twenty four) months?\b")]
    private static partial Regex MonthCountPattern();

    [GeneratedRegex(@"\b(this|current) month\b|\bmonth to date\b")]
    private static partial Regex CurrentMonthPattern();

    [GeneratedRegex(@"\b(last|previous|past) month\b")]
    private static partial Regex PreviousMonthPattern();

    [GeneratedRegex(@"\b(last|past|previous|this) quarter\b")]
    private static partial Regex QuarterPattern();

    [GeneratedRegex(@"\bhalf (a )?year\b")]
    private static partial Regex HalfYearPattern();

    [GeneratedRegex(@"\b(last|past|previous) year\b|\b(a|one) year\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\bthis year\b|\byear to date\b")]
    private static partial Regex ThisYearPattern();
}

/// <summary>Either a topic and period to answer, or a refusal (<see cref="RefusalReason"/>).</summary>
public record InsightMapping(InsightTopic? Topic, InsightPeriod? Period, string? RefusalReason)
{
    public static InsightMapping Answer(InsightTopic topic, InsightPeriod period) => new(topic, period, null);
    public static InsightMapping Refuse(string reason) => new(null, null, reason);
}

/// <summary>How many MYT months ending with the current one a period topic covers; <see cref="Focus"/> says which
/// of them the question was about (all, the current one only, or the previous one).</summary>
public record InsightPeriod(int MonthCount, PeriodFocus Focus, bool Stated)
{
    public static readonly InsightPeriod Default = new(AnalyticsRules.DefaultMonths, PeriodFocus.Whole, false);
    public static readonly InsightPeriod CurrentMonth = new(1, PeriodFocus.Whole, true);
    public static readonly InsightPeriod PreviousMonth = new(2, PeriodFocus.Previous, true);
    public static InsightPeriod Months(int count) => new(count, PeriodFocus.Whole, true);
}

public enum PeriodFocus
{
    /// <summary>Every month of the window.</summary>
    Whole,
    /// <summary>The month before the current one (the window's first month).</summary>
    Previous
}
