using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SarawakBizOps.Api.DTOs.Dashboard;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Analytics;
using SarawakBizOps.Api.Services.Parts;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 11 (PRD §6.9, SDD §14, AC-09): each named analytics operation over a small deterministic dataset with a
/// known answer, run by real SQL Server (the MYT month bucketing is SQL, so it is only proven here). Every test
/// gets its own scratch database — the operations count whole tables — and a fixed clock.
/// </summary>
[Collection(ApiCollection.Name)]
public class AnalyticsTests
{
    private static readonly DateTime MidSeptember = new(2026, 9, 15, 4, 0, 0, DateTimeKind.Utc); // 12:00 MYT

    private readonly ApiFactory _factory;

    public AnalyticsTests(ApiFactory factory) => _factory = factory;

    private static AnalyticsService Analytics(ScratchDatabase scratch, DateTime nowUtc)
        => new(scratch.NewContext(), new FixedClock(nowUtc));

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public async Task An_empty_database_gives_complete_series_of_zeros()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);

        var summary = await Analytics(scratch, MidSeptember).GetSummaryAsync(AnalyticsRules.DefaultMonths, default);

        Assert.Equal(new[] { "Urgent", "High", "Medium", "Low" }, summary.OpenRequestsByPriority.Select(c => c.Key));
        Assert.Equal(6, summary.WorkOrdersByStatus.Count);
        Assert.Equal(
            new[] { "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09" },
            summary.JobsCompletedPerMonth.Select(m => m.Month));
        Assert.All(summary.OpenRequestsByPriority, c => Assert.Equal(0, c.Count));
        Assert.All(summary.WorkOrdersByStatus, c => Assert.Equal(0, c.Count));
        Assert.All(summary.JobsCompletedPerMonth, m => Assert.Equal(0, m.Count));
        Assert.Equal((0, 0), (summary.LowStock.Count, summary.LowStock.OutOfStock));
        Assert.Empty(summary.LowStock.Items);
        Assert.Equal((0, 0, 0, 0, 0), (summary.Kpis.OpenRequests, summary.Kpis.ActiveWorkOrders,
            summary.Kpis.AwaitingApproval, summary.Kpis.CompletedThisMonth, summary.Kpis.LowStockParts));
        Assert.Equal(MidSeptember, summary.GeneratedAt);
    }

    [Fact]
    public async Task Open_requests_are_the_New_and_Approved_ones_per_priority()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        await scratch.AddRequestsAsync(
            (RequestPriority.Urgent, ServiceRequestStatus.New),
            (RequestPriority.Urgent, ServiceRequestStatus.Approved),
            (RequestPriority.High, ServiceRequestStatus.New),
            (RequestPriority.Low, ServiceRequestStatus.Approved),
            // Not open: followed through its job, or closed.
            (RequestPriority.High, ServiceRequestStatus.Assigned),
            (RequestPriority.Medium, ServiceRequestStatus.Rejected),
            (RequestPriority.Urgent, ServiceRequestStatus.Cancelled));

        var byPriority = await Analytics(scratch, MidSeptember).OpenRequestsByPriorityAsync(default);

        Assert.Equal(new[] { ("Urgent", 2), ("High", 1), ("Medium", 0), ("Low", 1) }, byPriority.Select(c => (c.Key, c.Count)));
    }

    [Fact]
    public async Task Work_orders_are_counted_per_status_cancelled_included()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        var done = Utc(2026, 9, 1);
        await scratch.AddWorkOrdersAsync(
            (WorkOrderStatus.Assigned, null), (WorkOrderStatus.Assigned, null),
            (WorkOrderStatus.InProgress, null),
            (WorkOrderStatus.PendingParts, null),
            (WorkOrderStatus.Completed, done),
            (WorkOrderStatus.Approved, done), (WorkOrderStatus.Approved, done), (WorkOrderStatus.Approved, done),
            (WorkOrderStatus.Cancelled, null));

        var summary = await Analytics(scratch, MidSeptember).GetSummaryAsync(AnalyticsRules.DefaultMonths, default);

        Assert.Equal(
            new[] { ("Assigned", 2), ("InProgress", 1), ("PendingParts", 1), ("Completed", 1), ("Approved", 3), ("Cancelled", 1) },
            summary.WorkOrdersByStatus.Select(c => (c.Key, c.Count)));
        Assert.Equal(4, summary.Kpis.ActiveWorkOrders);
        Assert.Equal(1, summary.Kpis.AwaitingApproval);
        Assert.Equal(4, summary.Kpis.CompletedThisMonth);
    }

    [Fact]
    public async Task Completed_jobs_are_bucketed_by_the_Malaysia_time_month()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        await scratch.AddWorkOrdersAsync(
            (WorkOrderStatus.Approved, Utc(2026, 8, 31, 15, 59, 59)),  // 23:59:59 MYT, 31 Aug → August
            (WorkOrderStatus.Approved, Utc(2026, 8, 31, 16)),          // 00:00 MYT, 1 Sep → September (a UTC bucket would say August)
            (WorkOrderStatus.Completed, Utc(2026, 6, 10, 2)),          // awaiting approval still counts
            (WorkOrderStatus.Approved, Utc(2026, 6, 30, 17)),          // 01:00 MYT, 1 Jul → July
            (WorkOrderStatus.Approved, Utc(2026, 3, 31, 16)),          // 00:00 MYT, 1 Apr: the first instant of the window
            (WorkOrderStatus.Approved, Utc(2026, 3, 31, 15, 59, 59)),  // 23:59:59 MYT, 31 Mar: just before it
            (WorkOrderStatus.Approved, Utc(2026, 9, 30, 16)),          // 00:00 MYT, 1 Oct: after the window
            (WorkOrderStatus.Cancelled, Utc(2026, 9, 2)),              // never a completion, whatever its timestamp
            (WorkOrderStatus.InProgress, null));

        var perMonth = await Analytics(scratch, MidSeptember).JobsCompletedPerMonthAsync(6, default);

        Assert.Equal(
            new[] { ("2026-04", 1), ("2026-05", 0), ("2026-06", 1), ("2026-07", 1), ("2026-08", 1), ("2026-09", 1) },
            perMonth.Select(m => (m.Month, m.Count)));
    }

    [Fact]
    public async Task The_month_trend_crosses_a_year_boundary_in_Malaysia_time()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        await scratch.AddWorkOrdersAsync(
            (WorkOrderStatus.Approved, Utc(2026, 11, 20)),
            (WorkOrderStatus.Approved, Utc(2026, 12, 31, 15, 30)), // 23:30 MYT, 31 Dec 2026
            (WorkOrderStatus.Approved, Utc(2026, 12, 31, 16, 30))); // 00:30 MYT, 1 Jan 2027

        var perMonth = await Analytics(scratch, Utc(2027, 1, 5)).JobsCompletedPerMonthAsync(3, default);

        Assert.Equal(new[] { ("2026-11", 1), ("2026-12", 1), ("2027-01", 1) }, perMonth.Select(m => (m.Month, m.Count)));
    }

    [Fact]
    public async Task Low_stock_summary_counts_active_parts_at_or_below_minimum_and_agrees_with_the_low_stock_list()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(_factory);
        await scratch.AddPartsAsync(
            ("P-OUT", 0, 5, true),        // out of stock, 5 below
            ("P-AT-MIN", 3, 3, true),     // exactly at its minimum: low
            ("P-FINE", 10, 2, true),
            ("P-INACTIVE", 1, 5, false),  // discontinued: never low
            ("P-WORST", 2, 10, true));    // 8 below: the most urgent

        var summary = await Analytics(scratch, MidSeptember).LowStockSummaryAsync(2, default);

        Assert.Equal((3, 1), (summary.Count, summary.OutOfStock));
        Assert.Equal(new[] { "P-WORST", "P-OUT" }, summary.Items.Select(i => i.PartNumber)); // top 2, most urgent first
        Assert.Equal((2m, 10m), (summary.Items[0].QuantityOnHand, summary.Items[0].MinimumStockLevel));

        // AC-09: the dashboard and GET /api/parts/low-stock use one rule, in one order.
        var all = await Analytics(scratch, MidSeptember).LowStockSummaryAsync(10, default);
        var list = await new PartService(scratch.NewContext(), NullLogger<PartService>.Instance).GetLowStockAsync(default);
        Assert.Equal(list.Select(p => p.PartNumber), all.Items.Select(i => i.PartNumber));
    }

    [Fact]
    public async Task A_part_that_falls_low_appears_on_the_Manager_dashboard()
    {
        // AC-09 end to end through the API: the shared database, so the assertion is on the change.
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var warehouse = await _factory.ClientWithRoleAsync(AppRoles.WarehouseStaff);
        var before = await manager.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary");

        var created = await warehouse.PostAsJsonAsync("/api/parts", new CreatePartRequest
        {
            PartNumber = $"DASH-{Guid.NewGuid():N}"[..20],
            Name = "Dashboard low-stock check",
            Unit = "pcs",
            UnitCost = 1m,
            MinimumStockLevel = 1_000_000m // far below its minimum, so it leads the list
        });
        created.EnsureSuccessStatusCode();
        var part = (await created.Content.ReadFromJsonAsync<PartDto>())!;

        var after = await manager.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary");
        Assert.Equal(before!.LowStock.Count + 1, after!.LowStock.Count);
        Assert.Equal(before.LowStock.OutOfStock + 1, after.LowStock.OutOfStock);
        Assert.Equal(after.LowStock.Count, after.Kpis.LowStockParts);
        Assert.Equal(part.PartNumber, after.LowStock.Items[0].PartNumber);
    }
}
