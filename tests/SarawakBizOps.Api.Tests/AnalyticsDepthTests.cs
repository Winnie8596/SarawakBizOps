using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Analytics;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 12 (PRD §6.9, §5): the remaining named analytics operations over small deterministic datasets with known
/// answers — each on its own scratch database with a fixed clock, boundaries included — and the role-shaped
/// payloads of GET /api/dashboard/summary.
/// </summary>
[Collection(ApiCollection.Name)]
public class AnalyticsDepthTests
{
    // 12:00 MYT on 15 September 2026: the six-month period is April–September, [31 Mar 16:00 UTC, 30 Sep 16:00 UTC).
    private static readonly DateTime MidSeptember = new(2026, 9, 15, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstInstant = Utc(2026, 3, 31, 16);        // 00:00 MYT, 1 April
    private static readonly DateTime JustBefore = Utc(2026, 3, 31, 15, 59, 59);  // 23:59:59 MYT, 31 March

    private readonly ApiFactory _factory;

    public AnalyticsDepthTests(ApiFactory factory) => _factory = factory;

    private static AnalyticsService Analytics(ScratchDatabase scratch, DateTime nowUtc)
        => new(scratch.NewContext(), new FixedClock(nowUtc));

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public async Task Nothing_to_measure_gives_no_averages_and_empty_lists()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        var analytics = Analytics(scratch, MidSeptember);

        var summary = await analytics.GetSummaryAsync(default);

        Assert.Equal(((double?)null, 0), (summary.AverageTimeToAssignment.AverageHours, summary.AverageTimeToAssignment.Count));
        Assert.Equal(((double?)null, 0), (summary.AverageCompletionDuration.AverageHours, summary.AverageCompletionDuration.Count));
        Assert.Equal((3, 0), (summary.RepeatedFailures.MinimumRequests, summary.RepeatedFailures.EquipmentCount));
        Assert.Empty(summary.RepeatedFailures.Items);
        Assert.Empty(summary.TopPartsByUsage);
        Assert.Equal(("2026-04", "2026-09"), (summary.Period.FromMonth, summary.Period.ToMonth));
        Assert.Equal((FirstInstant, Utc(2026, 9, 30, 16)), (summary.Period.FromUtc, summary.Period.ToUtc));

        var waiting = await analytics.JobsWaitingForPartsAsync(10, default);
        Assert.Equal((0, 0), (waiting.Count, waiting.ReadyToResume));
        var queue = await analytics.RequestQueueAsync(5, 30, default);
        Assert.Equal((0, 0, 0), (queue.AwaitingReview, queue.AwaitingAssignment, queue.RejectedRecently));
        Assert.Empty(queue.OldestOpen);
    }

    [Fact]
    public async Task Time_to_assignment_averages_every_assignment_in_the_period_from_when_the_request_was_raised()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        await scratch.AddAsync(
            scratch.Job(WorkOrderStatus.Assigned, Utc(2026, 9, 1), assignedAtUtc: Utc(2026, 9, 1, 6)),        // 6 h
            scratch.Job(WorkOrderStatus.Approved, Utc(2026, 8, 1), assignedAtUtc: Utc(2026, 8, 3)),            // 48 h
            scratch.Job(WorkOrderStatus.Cancelled, Utc(2026, 9, 10), assignedAtUtc: Utc(2026, 9, 10, 12)),     // 12 h: the assignment happened
            scratch.Job(WorkOrderStatus.Approved, Utc(2026, 3, 31, 10), assignedAtUtc: FirstInstant),          // 6 h: first instant counts
            scratch.Job(WorkOrderStatus.Approved, Utc(2026, 3, 1), assignedAtUtc: JustBefore),                 // assigned before the period
            scratch.Job(WorkOrderStatus.Assigned, Utc(2026, 9, 30, 10), assignedAtUtc: Utc(2026, 9, 30, 16)),  // assigned after it (1 Oct MYT)
            scratch.Job(WorkOrderStatus.New, Utc(2026, 9, 2)));                                                // never assigned

        var average = await Analytics(scratch, MidSeptember).AverageTimeToAssignmentAsync(6, default);

        Assert.Equal((18.0, 4), (average.AverageHours!.Value, average.Count)); // (6 + 48 + 12 + 6) / 4
    }

    [Fact]
    public async Task Completion_duration_averages_started_to_completed_over_jobs_completed_in_the_period()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        var raised = Utc(2026, 7, 1);
        await scratch.AddAsync(
            scratch.Job(WorkOrderStatus.Completed, raised, raised, Utc(2026, 9, 1), Utc(2026, 9, 1, 8)),          // 8 h, awaiting approval
            scratch.Job(WorkOrderStatus.Approved, raised, raised, Utc(2026, 8, 1, 9, 30), Utc(2026, 8, 2, 9, 30)), // 24 h
            scratch.Job(WorkOrderStatus.Approved, raised, raised, Utc(2026, 3, 31), FirstInstant),                 // 16 h: started before, completed on the first instant
            scratch.Job(WorkOrderStatus.Approved, raised, raised, Utc(2026, 3, 30), JustBefore),                   // completed before the period
            scratch.Job(WorkOrderStatus.InProgress, raised, raised, Utc(2026, 9, 2)),                              // not completed
            scratch.Job(WorkOrderStatus.Cancelled, raised, raised, Utc(2026, 9, 3), Utc(2026, 9, 3, 1)),           // never a completion
            scratch.Job(WorkOrderStatus.Approved, raised, raised, null, Utc(2026, 9, 4)));                         // no start recorded: nothing to measure

        var average = await Analytics(scratch, MidSeptember).AverageCompletionDurationAsync(6, default);

        Assert.Equal((16.0, 3), (average.AverageHours!.Value, average.Count)); // (8 + 24 + 16) / 3
    }

    [Fact]
    public async Task Repeated_failures_count_fault_reports_per_equipment_in_the_period_at_or_above_the_threshold()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        var a = scratch.EquipmentId;
        var b = await scratch.AddEquipmentAsync("SCR-B");
        var c = await scratch.AddEquipmentAsync("SCR-C");
        var d = await scratch.AddEquipmentAsync("SCR-D");
        await scratch.AddAsync(
            // A: exactly three fault reports (withdrawn and turned-down requests are not faults); last on 1 Sep.
            scratch.Request(ServiceRequestStatus.New, Utc(2026, 9, 1), a),
            scratch.Request(ServiceRequestStatus.Approved, Utc(2026, 6, 1), a),
            scratch.Request(ServiceRequestStatus.Assigned, FirstInstant, a),
            scratch.Request(ServiceRequestStatus.Cancelled, Utc(2026, 9, 2), a),
            scratch.Request(ServiceRequestStatus.Rejected, Utc(2026, 9, 3), a),
            // B: three reports, but one was raised before the period — two do not reach the threshold.
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 5, 1), b),
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 7, 1), b),
            scratch.Request(ServiceRequestStatus.Assigned, JustBefore, b),
            // C: four — the most.
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 4, 1), c),
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 5, 1), c),
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 6, 1), c),
            scratch.Request(ServiceRequestStatus.New, Utc(2026, 7, 1), c),
            // D: three, the latest more recent than A's — so D is listed before A.
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 4, 1), d),
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 5, 1), d),
            scratch.Request(ServiceRequestStatus.New, Utc(2026, 9, 10), d));

        var analytics = Analytics(scratch, MidSeptember);
        var failures = await analytics.RepeatedFailuresByEquipmentAsync(6, 3, 2, default);

        Assert.Equal((3, 3), (failures.MinimumRequests, failures.EquipmentCount)); // A, C, D — the list shows the top 2
        Assert.Equal(new[] { ("SCR-C", 4), ("SCR-D", 3) }, failures.Items.Select(i => (i.SerialNumber, i.RequestCount)));
        Assert.Equal(Utc(2026, 7, 1), failures.Items[0].LastReportedAt);
        Assert.Equal(("Scratch Customer", scratch.CustomerId), (failures.Items[0].CustomerName, failures.Items[0].CustomerId));

        var all = await analytics.RepeatedFailuresByEquipmentAsync(6, 3, 10, default);
        Assert.Equal(new[] { "SCR-C", "SCR-D", "SCR-001" }, all.Items.Select(i => i.SerialNumber));

        // A lower threshold brings B in: the threshold is a parameter, not a constant of the query.
        var twice = await analytics.RepeatedFailuresByEquipmentAsync(6, 2, 10, default);
        Assert.Equal(4, twice.EquipmentCount);
    }

    [Fact]
    public async Task Top_parts_are_ranked_by_cost_used_on_jobs_in_the_period()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        var raised = Utc(2026, 6, 1);
        var job1 = scratch.Job(WorkOrderStatus.Approved, raised, raised);
        var job2 = scratch.Job(WorkOrderStatus.Cancelled, raised, raised); // its parts left the store: they count
        var bearing = new Part { PartNumber = "BRG", Name = "Bearing", UnitCost = 12m };
        var oil = new Part { PartNumber = "OIL", Name = "Oil", Unit = "L", UnitCost = 5m };
        var fuse = new Part { PartNumber = "FUSE", Name = "Fuse", UnitCost = 0.333m };
        var old = new Part { PartNumber = "OLD", Name = "Used before the period", UnitCost = 1000m };
        WorkOrderPart Line(WorkOrder job, Part part, decimal quantity, decimal price, DateTime at)
            => new() { WorkOrder = job, Part = part, Quantity = quantity, UnitPrice = price, CreatedAt = at };
        await scratch.AddAsync(
            Line(job1, bearing, 2, 10m, Utc(2026, 6, 2)),      // RM 20 at the old price...
            Line(job2, bearing, 1.5m, 12m, Utc(2026, 9, 1)),   // ...RM 18 at the new: 3.5 on 2 jobs, RM 38
            Line(job1, oil, 10, 5m, FirstInstant),             // RM 50, on the first instant of the period
            Line(job1, fuse, 1, 0.333m, Utc(2026, 7, 1)),      // RM 0.333 → 0.33
            Line(job2, old, 1, 1000m, JustBefore));            // before the period

        var analytics = Analytics(scratch, MidSeptember);
        var top = await analytics.TopPartsByUsageAsync(6, 2, default);

        Assert.Equal(
            new[] { ("OIL", 10m, 50m, 1), ("BRG", 3.5m, 38m, 2) },
            top.Select(p => (p.PartNumber, p.Quantity, p.Cost, p.JobCount)));
        Assert.Equal(("Oil", "L"), (top[0].Name, top[0].Unit));

        var all = await analytics.TopPartsByUsageAsync(6, 10, default);
        Assert.Equal(new[] { "OIL", "BRG", "FUSE" }, all.Select(p => p.PartNumber));
        Assert.Equal(0.33m, all[2].Cost);
    }

    [Fact]
    public async Task Jobs_waiting_for_parts_are_listed_longest_first_with_whether_their_part_is_back()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        var raised = Utc(2026, 9, 1);
        var belt = new Part { PartNumber = "BELT", Name = "Belt", Unit = "m", QuantityOnHand = 5m };
        var seal = new Part { PartNumber = "SEAL", Name = "Seal", QuantityOnHand = 1m };
        WorkOrder Paused(Part part, decimal quantity, DateTime since, WorkOrderStatus status = WorkOrderStatus.PendingParts)
        {
            var job = scratch.Job(status, raised, raised, raised);
            (job.PendingPart, job.PendingPartQuantity, job.PendingPartsAt) = (part, quantity, since);
            return job;
        }
        var older = Paused(belt, 5m, Utc(2026, 9, 5));  // exactly enough on hand: ready to resume
        var newer = Paused(seal, 3m, Utc(2026, 9, 8));  // 1 of 3
        var resumed = Paused(seal, 1m, Utc(2026, 9, 2), WorkOrderStatus.InProgress); // the fields outlive the wait
        await scratch.AddAsync(newer, older, resumed);

        var waiting = await Analytics(scratch, MidSeptember).JobsWaitingForPartsAsync(10, default);

        Assert.Equal((2, 1), (waiting.Count, waiting.ReadyToResume));
        Assert.Equal(
            new[] { (older.Id, "BELT", 5m, 5m, true), (newer.Id, "SEAL", 3m, 1m, false) },
            waiting.Items.Select(w => (w.WorkOrderId, w.PartNumber, w.QuantityNeeded, w.QuantityOnHand, w.InStock)));
        Assert.Equal(Utc(2026, 9, 5), waiting.Items[0].WaitingSince);

        var top1 = await Analytics(scratch, MidSeptember).JobsWaitingForPartsAsync(1, default);
        Assert.Equal((2, 1), (top1.Count, top1.Items.Count)); // the count is all of them, the list is capped
    }

    [Fact]
    public async Task The_request_queue_lists_the_longest_waits_and_recent_rejections()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        ServiceRequest Rejected(DateTime at, string reason)
        {
            var r = scratch.Request(ServiceRequestStatus.Rejected, at.AddDays(-1));
            (r.RejectedAt, r.RejectionReason, r.RejectedByUserId) = (at, reason, scratch.UserId);
            return r;
        }
        var approved = scratch.Request(ServiceRequestStatus.Approved, Utc(2026, 9, 1), priority: RequestPriority.Urgent);
        var oldestNew = scratch.Request(ServiceRequestStatus.New, Utc(2026, 9, 10));
        await scratch.AddAsync(
            scratch.Request(ServiceRequestStatus.New, Utc(2026, 9, 12)),
            oldestNew,
            approved,
            scratch.Request(ServiceRequestStatus.Assigned, Utc(2026, 8, 1)),   // being worked: not queued
            scratch.Request(ServiceRequestStatus.Cancelled, Utc(2026, 8, 2)),
            Rejected(Utc(2026, 9, 1), "Out of warranty; customer to confirm."),
            Rejected(Utc(2026, 8, 16, 4), "Exactly 30 days ago: still recent."),
            Rejected(Utc(2026, 8, 16, 3, 59, 59), "Just over 30 days ago."));

        var queue = await Analytics(scratch, MidSeptember).RequestQueueAsync(2, 30, default);

        Assert.Equal((2, 1, 2, 30), (queue.AwaitingReview, queue.AwaitingAssignment, queue.RejectedRecently, queue.RecentDays));
        Assert.Equal(new[] { approved.Id, oldestNew.Id }, queue.OldestOpen.Select(r => r.Id));
        Assert.Equal(("Urgent", "Approved", "SCR-001", "Scratch Customer"),
            (queue.OldestOpen[0].Priority, queue.OldestOpen[0].Status, queue.OldestOpen[0].EquipmentSerialNumber,
             queue.OldestOpen[0].CustomerName));
        Assert.Equal(new[] { "Out of warranty; customer to confirm.", "Exactly 30 days ago: still recent." },
            queue.RecentlyRejected.Select(r => r.RejectionReason));
    }

    [Fact]
    public async Task Each_role_gets_only_its_own_view_of_the_dashboard()
    {
        // The shared database: the assertions are on the shape of each payload, not its numbers.
        var manager = await Json(await _factory.ClientWithRoleAsync(AppRoles.Manager));
        var admin = await Json(await _factory.ClientWithRoleAsync(AppRoles.Admin));
        var warehouse = await Json(await _factory.ClientWithRoleAsync(AppRoles.WarehouseStaff));
        var staff = await Json(await _factory.ClientWithRoleAsync(AppRoles.ServiceStaff));

        Assert.Equal("Manager", manager.GetProperty("view").GetString());
        Assert.Equal("Manager", admin.GetProperty("view").GetString());
        Assert.Equal(
            new[] { "view", "generatedAt", "period", "kpis", "openRequestsByPriority", "workOrdersByStatus",
                "jobsCompletedPerMonth", "lowStock", "averageTimeToAssignment", "averageCompletionDuration",
                "repeatedFailures", "topPartsByUsage" },
            Names(manager));

        // Stock, not the Manager's turnaround, statuses or request queue.
        Assert.Equal("Warehouse", warehouse.GetProperty("view").GetString());
        Assert.Equal(
            new[] { "view", "generatedAt", "period", "kpis", "lowStock", "jobsWaitingForParts", "topPartsByUsage" },
            Names(warehouse));
        Assert.Equal(new[] { "activeParts", "lowStockParts", "outOfStockParts", "jobsWaitingForParts" },
            Names(warehouse.GetProperty("kpis")));

        // The queue, not stock, costs or work orders.
        Assert.Equal("ServiceQueue", staff.GetProperty("view").GetString());
        Assert.Equal(new[] { "view", "generatedAt", "kpis", "openRequestsByPriority", "queue" }, Names(staff));
        Assert.Equal(new[] { "awaitingReview", "awaitingAssignment", "rejectedRecently" }, Names(staff.GetProperty("kpis")));

        var technician = await _factory.ClientWithRoleAsync(AppRoles.Technician);
        Assert.Equal(HttpStatusCode.Forbidden, (await technician.GetAsync("/api/dashboard/summary")).StatusCode);

        static async Task<JsonElement> Json(HttpClient client)
            => await client.GetFromJsonAsync<JsonElement>("/api/dashboard/summary");

        static string[] Names(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();
    }

    [Fact]
    public async Task A_new_request_joins_the_service_queue()
    {
        var staff = await _factory.ClientWithRoleAsync(AppRoles.ServiceStaff);
        var before = await staff.GetFromJsonAsync<ServiceQueueDashboardDto>("/api/dashboard/summary");

        await CreateNewRequestAsync(await _factory.AdminClientAsync());

        var after = await staff.GetFromJsonAsync<ServiceQueueDashboardDto>("/api/dashboard/summary");
        Assert.Equal(before!.Kpis.AwaitingReview + 1, after!.Kpis.AwaitingReview);
        Assert.Equal(after.Queue.AwaitingReview, after.Kpis.AwaitingReview);
        Assert.Equal(after.Kpis.AwaitingReview + after.Kpis.AwaitingAssignment, after.OpenRequestsByPriority.Sum(c => c.Count));
    }
}
