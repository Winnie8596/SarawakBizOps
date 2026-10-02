namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The allow-list of what the Manager AI (Phase 14, AI-02) can be asked about: each topic is answered by one of the
/// existing named analytics operations (<see cref="Analytics.IAnalyticsService"/>), and nothing else is. A question
/// that maps to no topic, or to two equally well, is refused with this list — the model is never asked to work out
/// on its own what the data might say.
/// </summary>
public static class ManagerInsightTopics
{
    public const string JobsCompleted = "jobs-completed";
    public const string WorkOrdersByStatus = "work-orders-by-status";
    public const string OpenRequests = "open-requests";
    public const string LowStock = "low-stock";
    public const string TimeToAssignment = "time-to-assignment";
    public const string CompletionDuration = "completion-duration";
    public const string RepeatedFailures = "repeated-failures";
    public const string TopParts = "top-parts";
    public const string JobsWaitingForParts = "jobs-waiting-for-parts";

    /// <summary>In the order the UI lists them. <see cref="InsightTopic.UsesPeriod"/> topics are measured over
    /// whole Malaysia-time months; the others are a snapshot of now.</summary>
    public static readonly IReadOnlyList<InsightTopic> All = new InsightTopic[]
    {
        new(JobsCompleted, "Jobs completed",
            "Jobs completed per month (Completed or Approved).",
            "How many jobs were completed last month?", UsesPeriod: true,
            new Phrase[] { new("complet", 2), new("finish", 2), new("jobs done", 2), new("closed jobs", 2) }),
        new(WorkOrdersByStatus, "Work orders by status",
            "How many jobs are in each status now, and how many await your approval.",
            "How many jobs are awaiting approval?", UsesPeriod: false,
            new Phrase[]
            {
                new("status", 2), new("in progress", 3), new("approval", 2), new("approved", 2), new("active job", 3),
                new("open job", 3), new("assigned jobs", 4), new("jobs are assigned", 4), new("cancelled", 2),
                new("work orders", 1)
            }),
        new(OpenRequests, "Open service requests",
            "Requests awaiting review or assignment, by priority.",
            "How many open requests are there by priority?", UsesPeriod: false,
            new Phrase[]
            {
                new("request", 2), new("open request", 3), new("urgent", 2), new("backlog", 2),
                new("queue", 2), new("priorit", 2), new("awaiting review", 3)
            }),
        new(LowStock, "Low stock",
            "Active parts at or below their minimum stock level.",
            "Which parts are low on stock?", UsesPeriod: false,
            new Phrase[]
            {
                new("low stock", 4), new("low on stock", 4), new("stock", 2), new("out of stock", 4),
                new("reorder", 3), new("running low", 3), new("inventory", 2), new("below minimum", 3), new("low", 2)
            }),
        new(TimeToAssignment, "Time to assignment",
            "Average time from a request being raised to its job being assigned.",
            "How long does it take to assign a request?", UsesPeriod: true,
            new Phrase[]
            {
                new("assign", 3), new("response time", 3), new("wait for a technician", 3),
                new("customers wait", 3), new("wait", 1)
            }),
        new(CompletionDuration, "Completion duration",
            "Average time from a job being started to being completed.",
            "How long do jobs take to complete?", UsesPeriod: true,
            new Phrase[]
            {
                new("how long", 1), new("duration", 2), new("take to complet", 3), new("long to complet", 3),
                new("time to complet", 3), new("completion time", 3), new("time on the job", 3), new("turnaround", 3),
                new("average completion", 3), new("repair time", 3), new("to fix", 2)
            }),
        new(RepeatedFailures, "Repeated failures",
            "Equipment with three or more fault reports in the period.",
            "Which equipment keeps failing?", UsesPeriod: true,
            new Phrase[]
            {
                new("repeat", 3), new("recurring", 3), new("keeps failing", 3), new("keep failing", 3),
                new("fail", 2), new("breakdown", 2), new("break down", 2), new("unreliable", 3), new("fault", 2)
            }),
        new(TopParts, "Parts usage",
            "The parts that cost the most on jobs, with the quantity used.",
            "Which parts did we use the most in the last 3 months?", UsesPeriod: true,
            new Phrase[]
            {
                new("parts used", 3), new("parts usage", 3), new("most used", 3), new("top parts", 3),
                new("spent on parts", 3), new("parts cost", 3), new("cost", 2), new("spend", 2), new("consum", 2),
                new("use the most", 3), new("used the most", 3), new("parts", 1)
            }),
        new(JobsWaitingForParts, "Jobs waiting for parts",
            "Jobs paused for a part, and whether the part is back in stock.",
            "Which jobs are waiting for parts?", UsesPeriod: false,
            new Phrase[]
            {
                new("waiting for parts", 4), new("waiting for a part", 4), new("waiting on parts", 4),
                new("pending parts", 4), new("awaiting parts", 4), new("paused", 3), new("on hold", 3)
            })
    };

    public static InsightTopic Get(string id) => All.Single(t => t.Id == id);
}

/// <param name="Phrases">What a question about this topic says: matched at the start of a word (so "complet"
/// covers complete, completed and completion), each with a weight — a phrase that names the topic outright weighs
/// more than a word several topics share.</param>
public record InsightTopic(
    string Id, string Title, string Description, string ExampleQuestion, bool UsesPeriod,
    IReadOnlyList<Phrase> Phrases);

public record Phrase(string Text, int Weight);
