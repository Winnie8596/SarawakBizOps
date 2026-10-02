using System.Globalization;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// Everything the model is shown for one Manager question (Phase 14, AI-02, AC-10) — and exactly what the Manager
/// is shown beside its summary. Only figures an analytics operation computed, each with a label and a unit; the
/// question itself, raw rows, customer names and serial numbers never reach it.
/// </summary>
/// <param name="Note">Something the Manager should know about how the question was read, e.g. that a snapshot
/// topic ignores the period asked for.</param>
public record InsightFacts(string Topic, string Period, string Definition, IReadOnlyList<InsightFigure> Figures, string? Note);

/// <param name="Value">Null when there was nothing to measure (e.g. an average over no jobs).</param>
public record InsightFigure(string Label, decimal? Value, string Unit);

/// <summary>
/// Runs the one allow-listed analytics operation behind a topic and turns its result into <see cref="InsightFacts"/>.
/// Every number is copied from the operation's result or derived by the same rules the dashboard uses
/// (<see cref="AnalyticsRules"/>) — the model never computes one.
/// </summary>
public static class ManagerInsightFactsBuilder
{
    /// <summary>How many items (parts, equipment, jobs) a list topic shows.</summary>
    public const int ListItems = AnalyticsRules.ManagerListItems;

    public const string SnapshotPeriod = "Now";

    public const string SnapshotNote = "These are current figures; they do not depend on the period you asked about.";

    public static async Task<(InsightFacts Facts, AnalyticsPeriodDto? Period)> BuildAsync(
        IAnalyticsService analytics, InsightTopic topic, InsightPeriod period, DateTime nowUtc, CancellationToken ct)
    {
        if (!topic.UsesPeriod)
        {
            var note = period.Stated ? SnapshotNote : null;
            var (definition, figures) = await SnapshotAsync(analytics, topic.Id, ct);
            return (new InsightFacts(topic.Title, SnapshotPeriod, definition, figures, note), null);
        }

        var window = AnalyticsRules.MonthsEnding(nowUtc, period.MonthCount);
        var label = PeriodLabel(window);
        var months = period.MonthCount;

        (string Definition, List<InsightFigure> Figures, string Period, string? Note) result = topic.Id switch
        {
            ManagerInsightTopics.JobsCompleted => await JobsCompletedAsync(analytics, window, period, ct),
            ManagerInsightTopics.TimeToAssignment => Duration(
                await analytics.AverageTimeToAssignmentAsync(months, ct),
                "Average time from request to assignment", "Jobs assigned in the period",
                "From a request being raised to its job being assigned, over every job assigned in the period " +
                "(a job cancelled later included).",
                label, period),
            ManagerInsightTopics.CompletionDuration => Duration(
                await analytics.AverageCompletionDurationAsync(months, ct),
                "Average time from start to completion", "Jobs completed in the period",
                "From a job being started to being completed, over jobs completed in the period; time waiting " +
                "for parts counts.",
                label, period),
            ManagerInsightTopics.RepeatedFailures => RepeatedFailures(
                await analytics.RepeatedFailuresByEquipmentAsync(
                    months, AnalyticsRules.DefaultRepeatedFailureThreshold, ListItems, ct),
                label, period),
            ManagerInsightTopics.TopParts => TopParts(await analytics.TopPartsByUsageAsync(months, ListItems, ct), label, period),
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic.Id, "Not a period topic.")
        };

        return (new InsightFacts(topic.Title, result.Period, result.Definition, result.Figures, result.Note),
            AnalyticsRules.Period(window));
    }

    private static async Task<(string, List<InsightFigure>)> SnapshotAsync(IAnalyticsService analytics, string topicId, CancellationToken ct)
    {
        switch (topicId)
        {
            case ManagerInsightTopics.WorkOrdersByStatus:
            {
                var byStatus = await analytics.WorkOrdersByStatusAsync(ct);
                int Count(WorkOrderStatus s) => byStatus.First(c => c.Key == s.ToString()).Count;
                var figures = new List<InsightFigure>
                {
                    new("Active jobs (assigned, in progress or waiting for parts)",
                        AnalyticsRules.ActiveJobStatuses.Sum(Count), "jobs"),
                    new("Awaiting your approval (completed)", Count(WorkOrderStatus.Completed), "jobs")
                };
                figures.AddRange(byStatus.Select(c => new InsightFigure($"Status: {StatusLabel(c.Key)}", c.Count, "jobs")));
                return ("Every work order by its current status, cancelled ones included.", figures);
            }
            case ManagerInsightTopics.OpenRequests:
            {
                var byPriority = await analytics.OpenRequestsByPriorityAsync(ct);
                var figures = new List<InsightFigure> { new("Open requests", byPriority.Sum(c => c.Count), "requests") };
                figures.AddRange(byPriority.Select(c => new InsightFigure($"Priority: {c.Key}", c.Count, "requests")));
                return ("Open = awaiting review (New) or approved and awaiting assignment.", figures);
            }
            case ManagerInsightTopics.LowStock:
            {
                var low = await analytics.LowStockSummaryAsync(ListItems, ct);
                var figures = new List<InsightFigure>
                {
                    new("Active parts at or below their minimum", low.Count, "parts"),
                    new("Of those, out of stock", low.OutOfStock, "parts")
                };
                figures.AddRange(low.Items.Select(p => new InsightFigure(
                    $"{p.PartNumber} {p.Name}: on hand (minimum {Number(p.MinimumStockLevel)})", p.QuantityOnHand, p.Unit)));
                return ($"Active parts whose stock on hand is at or below their minimum level; the {ListItems} furthest " +
                    "below it are listed.", figures);
            }
            case ManagerInsightTopics.JobsWaitingForParts:
            {
                var waiting = await analytics.JobsWaitingForPartsAsync(ListItems, ct);
                var figures = new List<InsightFigure>
                {
                    new("Jobs waiting for parts", waiting.Count, "jobs"),
                    new("Of those, part back in stock (ready to resume)", waiting.ReadyToResume, "jobs")
                };
                figures.AddRange(waiting.Items.Select(w => new InsightFigure(
                    $"Job #{w.WorkOrderId} waiting for {w.PartNumber} {w.Name} ({(w.InStock ? "in stock now" : "still short")}): needed",
                    w.QuantityNeeded, w.Unit)));
                return ($"Jobs paused for a part, the longest-waiting first ({ListItems} listed).", figures);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(topicId), topicId, "Not a snapshot topic.");
        }
    }

    private static async Task<(string, List<InsightFigure>, string, string?)> JobsCompletedAsync(
        IAnalyticsService analytics, MonthWindow window, InsightPeriod period, CancellationToken ct)
    {
        const string definition = "A job counts once the technician has completed it (Completed or Approved), in the " +
            "Malaysia-time month it was completed.";
        var perMonth = await analytics.JobsCompletedPerMonthAsync(period.MonthCount, ct);

        if (period.Focus == PeriodFocus.Previous)
        {
            var previous = perMonth[0];
            var month = MonthLabel(window.Months[0]);
            return (definition, new List<InsightFigure> { new($"Jobs completed in {month}", previous.Count, "jobs") }, month, null);
        }

        var label = PeriodLabel(window);
        if (perMonth.Count == 1)
        {
            return (definition,
                new List<InsightFigure> { new($"Jobs completed in {label} (so far)", perMonth[0].Count, "jobs") }, label, null);
        }

        var figures = new List<InsightFigure> { new($"Jobs completed, {label}", perMonth.Sum(m => m.Count), "jobs") };
        figures.AddRange(perMonth.Select((m, i) => new InsightFigure(MonthLabel(window.Months[i]), m.Count, "jobs")));
        return (definition, figures, label, null);
    }

    private static (string, List<InsightFigure>, string, string?) Duration(
        DurationStatDto stat, string averageLabel, string countLabel, string definition, string label, InsightPeriod period)
        => (definition,
            new List<InsightFigure>
            {
                new(averageLabel, stat.AverageHours is { } hours ? (decimal)hours : null, "hours"),
                new(countLabel, stat.Count, "jobs")
            },
            label, PreviousMonthNote(period, label));

    private static (string, List<InsightFigure>, string, string?) RepeatedFailures(
        RepeatedFailuresDto failures, string label, InsightPeriod period)
    {
        var figures = new List<InsightFigure>
        {
            new($"Equipment with {failures.MinimumRequests} or more fault reports", failures.EquipmentCount, "machines")
        };
        // Equipment by type and id only: no customer name or serial number goes to the model.
        figures.AddRange(failures.Items.Select(e => new InsightFigure(
            $"{e.EquipmentType} (equipment #{e.EquipmentId})", e.RequestCount, "fault reports")));
        return ("A fault report is a service request that was not cancelled or rejected, by when it was raised; " +
            $"the {ListItems} pieces of equipment with the most are listed.", figures, label, PreviousMonthNote(period, label));
    }

    private static (string, List<InsightFigure>, string, string?) TopParts(
        IReadOnlyList<PartUsageDto> parts, string label, InsightPeriod period)
    {
        var figures = new List<InsightFigure>();
        if (parts.Count == 0)
        {
            figures.Add(new InsightFigure("Parts used on jobs", 0, "parts"));
        }
        foreach (var p in parts)
        {
            figures.Add(new InsightFigure($"{p.PartNumber} {p.Name}: cost", p.Cost, "RM"));
            figures.Add(new InsightFigure($"{p.PartNumber} {p.Name}: quantity used", p.Quantity, p.Unit));
        }
        return ($"Parts used on jobs in the period, net of returns, at the prices when they were used; the {ListItems} " +
            "costliest are listed.", figures, label, PreviousMonthNote(period, label));
    }

    // These operations measure whole months up to now, so "last month" is answered with last month and this one.
    private static string? PreviousMonthNote(InsightPeriod period, string label)
        => period.Focus == PeriodFocus.Previous
            ? $"This figure is kept for whole months up to today, so it covers {label}, not last month alone."
            : null;

    /// <summary>"Sep 2026", "Apr–Sep 2026" or "Nov 2025–Apr 2026".</summary>
    public static string PeriodLabel(MonthWindow window)
    {
        var (first, last) = (window.Months[0], window.Months[^1]);
        if (first == last) return MonthLabel(first);
        return first.Year == last.Year
            ? $"{ShortMonth(first.Month)}–{MonthLabel(last)}"
            : $"{MonthLabel(first)}–{MonthLabel(last)}";
    }

    private static string MonthLabel((int Year, int Month) m) => $"{ShortMonth(m.Month)} {m.Year}";

    private static string ShortMonth(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);

    private static string StatusLabel(string status) => status switch
    {
        nameof(WorkOrderStatus.InProgress) => "In progress",
        nameof(WorkOrderStatus.PendingParts) => "Pending parts",
        _ => status
    };

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
