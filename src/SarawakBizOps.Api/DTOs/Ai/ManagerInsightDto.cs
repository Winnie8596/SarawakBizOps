using System.ComponentModel.DataAnnotations;
using SarawakBizOps.Api.DTOs.Dashboard;

namespace SarawakBizOps.Api.DTOs.Ai;

/// <summary>Asks the Manager AI one question (Phase 14, AI-02).</summary>
public class ManagerInsightsRequest
{
    /// <summary>Read only by the server's rule-based mapper; it is never sent to the model.</summary>
    [Required]
    [MaxLength(300)]
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// The answer to a Manager question (AI-02, AC-10). <see cref="Outcome"/> is <c>Answered</c> — the figures an
/// analytics operation computed, with the model's wording of them beside — or <c>Refused</c>, with the reason and
/// what can be asked instead. The <see cref="Figures"/> are exactly what the model was given; its
/// <see cref="Summary"/> may only repeat their numbers, and is null (with <see cref="SummaryUnavailableReason"/>)
/// when the model failed, timed out or broke that rule — the figures are shown either way.
/// </summary>
public class ManagerInsightDto
{
    public string Outcome { get; set; } = ManagerInsightOutcomes.Answered;
    public string Question { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    // ---- Answered ----
    public ManagerInsightTopicDto? Topic { get; set; }

    /// <summary>As the figures label it: "Sep 2026", "Apr–Sep 2026", or "Now" for a snapshot topic.</summary>
    public string? PeriodLabel { get; set; }

    /// <summary>The Malaysia-time months covered; null for a snapshot topic.</summary>
    public AnalyticsPeriodDto? Period { get; set; }

    public string? Definition { get; set; }
    public string? Note { get; set; }
    public List<ManagerInsightFigureDto> Figures { get; set; } = new();
    public string? Summary { get; set; }
    public string? SummaryUnavailableReason { get; set; }
    public string? Disclaimer { get; set; }

    /// <summary>Who wrote the summary, e.g. "Demo mock assistant"; null when there is none.</summary>
    public string? GeneratedBy { get; set; }

    // ---- Refused ----
    public string? RefusalReason { get; set; }
    public List<ManagerInsightTopicDto> SupportedTopics { get; set; } = new();
}

public static class ManagerInsightOutcomes
{
    public const string Answered = "Answered";
    public const string Refused = "Refused";
}

public class ManagerInsightFigureDto
{
    public string Label { get; set; } = string.Empty;

    /// <summary>Null when there was nothing to measure.</summary>
    public decimal? Value { get; set; }

    /// <summary>"jobs", "hours", "RM", a part's unit…</summary>
    public string Unit { get; set; } = string.Empty;
}

public class ManagerInsightTopicDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExampleQuestion { get; set; } = string.Empty;
}
