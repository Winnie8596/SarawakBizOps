using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Inventory;
using SarawakBizOps.Api.DTOs.Parts;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 6: the parts catalogue and the inventory ledger (PRD §6.5 first half, SDD §10). AC-09 at API level,
/// BR-09 (one immutable ledger row per stock change, committed together with it) and the never-negative
/// stock rule under concurrency. Every test that moves stock ends by reconciling the WHOLE database —
/// QuantityOnHand == SUM(ledger) for every part — so no test can leave drift behind unnoticed.
/// </summary>
[Collection(ApiCollection.Name)]
public class InventoryTests
{
    private readonly ApiFactory _factory;

    public InventoryTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> WarehouseAsync() => _factory.ClientWithRoleAsync(AppRoles.WarehouseStaff);

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    private static async Task<List<InventoryTransactionDto>> LedgerAsync(HttpClient client, int partId)
        => (await client.GetFromJsonAsync<List<InventoryTransactionDto>>($"/api/inventory-transactions?partId={partId}"))!;

    private static UpdatePartRequest EditOf(PartDto part, Action<UpdatePartRequest>? change = null)
    {
        var edit = new UpdatePartRequest
        {
        PartNumber = part.PartNumber,
        Name = part.Name,
        Description = part.Description,
        Unit = part.Unit,
        UnitCost = part.UnitCost,
        MinimumStockLevel = part.MinimumStockLevel,
        IsActive = part.IsActive,
        RowVersion = part.RowVersion
        };
        change?.Invoke(edit);
        return edit;
    }

    // ---- Parts catalogue ----

    [Fact]
    public async Task Warehouse_creates_a_part_that_starts_with_no_stock_and_no_ledger_rows()
    {
        var warehouse = await WarehouseAsync();
        var number = $"RLY-{Guid.NewGuid():N}"[..16];

        var response = await PostPartAsync(warehouse, number, minimumStockLevel: 2, unitCost: 145.00m, unit: "pcs", name: "Overload relay");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var part = (await response.Content.ReadFromJsonAsync<PartDto>())!;
        Assert.Equal(number, part.PartNumber);
        Assert.Equal("Overload relay", part.Name);
        Assert.Equal(145.00m, part.UnitCost);
        Assert.Equal(0m, part.QuantityOnHand);
        Assert.Equal(2m, part.MinimumStockLevel);
        Assert.True(part.IsActive);
        Assert.True(part.IsLowStock); // 0 on hand is at or below a minimum of 2
        Assert.NotEmpty(part.RowVersion);
        Assert.Equal($"/api/parts/{part.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Empty(await LedgerAsync(warehouse, part.Id));
    }

    [Fact]
    public async Task Part_numbers_are_unique_regardless_of_case()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreatePartAsync(warehouse);

        var duplicate = await PostPartAsync(warehouse, part.PartNumber.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("already in use", await DetailAsync(duplicate));
    }

    [Fact]
    public async Task Renaming_a_part_onto_another_parts_number_is_a_409()
    {
        var warehouse = await WarehouseAsync();
        var first = await CreatePartAsync(warehouse);
        var second = await CreatePartAsync(warehouse);

        var response = await warehouse.PutAsJsonAsync($"/api/parts/{second.Id}", EditOf(second, e => e.PartNumber = first.PartNumber));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(second.PartNumber, (await GetPartAsync(warehouse, second.Id)).PartNumber);
    }

    [Theory]
    [InlineData(-1, 5, "Unit cost cannot be negative.")]
    [InlineData(10.005, 5, "Unit cost can have at most 2 decimal places (sen).")]
    [InlineData(10, -1, "Minimum stock level cannot be negative.")]
    [InlineData(10, 1.0005, "Minimum stock level can have at most 3 decimal places.")]
    public async Task Invalid_money_or_quantity_values_are_a_400(double unitCost, double minimum, string expected)
    {
        var warehouse = await WarehouseAsync();

        var response = await PostPartAsync(warehouse, unitCost: (decimal)unitCost, minimumStockLevel: (decimal)minimum);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await DetailAsync(response));
    }

    [Fact]
    public async Task Editing_a_part_changes_the_catalogue_fields_but_never_the_stock()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 7);

        // A crafted body that also tries to set the stock: the field is not part of the contract.
        var response = await warehouse.PutAsJsonAsync($"/api/parts/{part.Id}", new
        {
            partNumber = part.PartNumber,
            name = "Renamed part",
            description = "Now with a description",
            unit = "box",
            unitCost = 99.90m,
            minimumStockLevel = 3m,
            isActive = true,
            quantityOnHand = 500m,
            rowVersion = part.RowVersion
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<PartDto>())!;
        Assert.Equal("Renamed part", updated.Name);
        Assert.Equal("box", updated.Unit);
        Assert.Equal(99.90m, updated.UnitCost);
        Assert.Equal(3m, updated.MinimumStockLevel);
        Assert.Equal(7m, updated.QuantityOnHand);
        Assert.NotEqual(part.RowVersion, updated.RowVersion);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task Editing_a_part_with_a_stale_row_version_is_a_409_and_a_missing_one_is_a_400()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreatePartAsync(warehouse);
        // Receiving stock bumps the part's RowVersion, so the screen the edit was made on is now stale.
        await ReceiveAsync(warehouse, part.Id, 4);

        var stale = await warehouse.PutAsJsonAsync($"/api/parts/{part.Id}", EditOf(part, e => e.Name = "Stale edit"));
        var missing = await warehouse.PutAsJsonAsync($"/api/parts/{part.Id}", EditOf(part, e => e.RowVersion = ""));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("changed by someone else", await DetailAsync(stale));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(part.Name, (await GetPartAsync(warehouse, part.Id)).Name);
    }

    [Fact]
    public async Task Inactive_parts_are_hidden_from_the_list_unless_asked_for_and_search_matches_number_or_name()
    {
        var warehouse = await WarehouseAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var active = await CreatePartAsync(warehouse, partNumber: $"ACT-{marker}", name: "Search target active");
        var retired = await CreatePartAsync(warehouse, name: $"Retired bearing {marker}");
        await warehouse.PutAsJsonAsync($"/api/parts/{retired.Id}", EditOf(retired, e => e.IsActive = false));

        var byNumber = await warehouse.GetFromJsonAsync<List<PartDto>>($"/api/parts?search={marker}");
        var withInactive = await warehouse.GetFromJsonAsync<List<PartDto>>($"/api/parts?search={marker}&includeInactive=true");

        Assert.Equal(new[] { active.Id }, byNumber!.Select(p => p.Id));
        Assert.Equal(new[] { active.Id, retired.Id }.Order(), withInactive!.Select(p => p.Id).Order());
        Assert.False(withInactive!.Single(p => p.Id == retired.Id).IsActive);
    }

    // ---- Stock movements (BR-09) ----

    [Fact]
    public async Task Receiving_stock_writes_exactly_one_ledger_row_and_raises_the_stock_in_step()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreatePartAsync(warehouse, unit: "L");

        var response = await PostStockMovementAsync(warehouse, part.Id, "Receive", 12.5m, "Delivery note DN-1001");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = (await response.Content.ReadFromJsonAsync<InventoryTransactionDto>())!;
        Assert.Equal("Receive", entry.TransactionType);
        Assert.Equal(12.5m, entry.Quantity);
        Assert.Equal(12.5m, entry.QuantityOnHandAfter);
        Assert.Equal("Delivery note DN-1001", entry.Notes);
        Assert.Equal("Test WarehouseStaff", entry.CreatedByName); // the actor comes from the token
        Assert.Null(entry.WorkOrderId);

        Assert.Equal(12.5m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        Assert.Single(await LedgerAsync(warehouse, part.Id));
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task An_adjustment_with_a_reason_moves_stock_either_way()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 10);

        var down = await PostStockMovementAsync(warehouse, part.Id, "Adjustment", -2.25m, "Stocktake: two and a quarter short");
        var up = await PostStockMovementAsync(warehouse, part.Id, "Adjustment", 1m, "Found one in the wrong bin");

        Assert.Equal(HttpStatusCode.Created, down.StatusCode);
        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        Assert.Equal(8.75m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        Assert.Equal(3, (await LedgerAsync(warehouse, part.Id)).Count);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task An_adjustment_without_a_reason_is_rejected_and_changes_nothing()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 6);

        var response = await PostStockMovementAsync(warehouse, part.Id, "Adjustment", -1, notes: "  ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A reason is required for a stock adjustment.", await DetailAsync(response));
        Assert.Equal(6m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        Assert.Single(await LedgerAsync(warehouse, part.Id)); // just the original receive
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task An_adjustment_that_would_take_stock_below_zero_is_a_409_and_changes_nothing()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 3);

        var response = await PostStockMovementAsync(warehouse, part.Id, "Adjustment", -4, "Write-off");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("only 3 pcs on hand", await DetailAsync(response));
        Assert.Equal(3m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        Assert.Single(await LedgerAsync(warehouse, part.Id));
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Theory]
    [InlineData("Issue")]
    [InlineData("Return")]
    public async Task Issue_and_return_cannot_be_created_through_the_inventory_endpoint(string type)
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 5);

        var response = await PostStockMovementAsync(warehouse, part.Id, type, type == "Issue" ? -1 : 1, "Sneaky");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("work order", await DetailAsync(response));
        Assert.Equal(5m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
    }

    [Theory]
    [InlineData("Receive", 0, "greater than zero")]
    [InlineData("Receive", -5, "greater than zero")]
    [InlineData("Adjustment", 0, "must change the stock")]
    [InlineData("Receive", 1.2345, "decimal places")]
    [InlineData("Shrinkage", 1, "Receive or Adjustment")]
    public async Task Malformed_movements_are_a_400(string type, double quantity, string expected)
    {
        var warehouse = await WarehouseAsync();
        var part = await CreatePartAsync(warehouse);

        var response = await PostStockMovementAsync(warehouse, part.Id, type, (decimal)quantity, "note");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await DetailAsync(response));
        Assert.Empty(await LedgerAsync(warehouse, part.Id));
    }

    [Fact]
    public async Task A_movement_for_an_unknown_part_is_a_404()
    {
        var warehouse = await WarehouseAsync();

        var response = await PostStockMovementAsync(warehouse, int.MaxValue, "Receive", 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_inactive_part_cannot_receive_stock_but_its_leftovers_can_be_written_off()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 4);
        await warehouse.PutAsJsonAsync($"/api/parts/{part.Id}", EditOf(part, e => e.IsActive = false));

        var receive = await PostStockMovementAsync(warehouse, part.Id, "Receive", 10);
        var writeOff = await PostStockMovementAsync(warehouse, part.Id, "Adjustment", -4, "Discontinued; scrapped");

        Assert.Equal(HttpStatusCode.Conflict, receive.StatusCode);
        Assert.Contains("inactive", await DetailAsync(receive));
        Assert.Equal(HttpStatusCode.Created, writeOff.StatusCode);
        Assert.Equal(0m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task Concurrent_write_offs_of_the_last_units_never_take_stock_below_zero()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 3);

        // Eight simultaneous one-unit write-offs race for three units: exactly three can win.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => PostStockMovementAsync(warehouse, part.Id, "Adjustment", -1, $"Racing write-off {i}")));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        Assert.Equal(4, (await LedgerAsync(warehouse, part.Id)).Count); // the receive + three winners
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    [Fact]
    public async Task Concurrent_receives_all_land_and_the_total_is_exact()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreatePartAsync(warehouse);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => PostStockMovementAsync(warehouse, part.Id, "Receive", 1.5m, "Parallel delivery")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(15m, (await GetPartAsync(warehouse, part.Id)).QuantityOnHand);
        await LedgerReconciliation.AssertReconcilesAsync(_factory.Services);
    }

    // ---- The ledger is immutable (BR-09) ----

    [Fact]
    public async Task No_route_can_edit_or_delete_a_ledger_row()
    {
        // Structural proof: the only inventory-transaction routes the app exposes are GET and POST on the
        // collection. A future PUT/PATCH/DELETE — or a GET/POST on /{id} — would fail this test.
        var routes = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/inventory-transactions", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? new[] { "*" })
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Order()
            .ToList();

        Assert.Equal(new[] { "GET api/inventory-transactions", "POST api/inventory-transactions" }, routes);

        // And behaviourally: attempts to change an existing row go nowhere and leave it as it was.
        var admin = await _factory.AdminClientAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 2);
        var entry = (await LedgerAsync(admin, part.Id)).Single();

        var put = await admin.PutAsJsonAsync($"/api/inventory-transactions/{entry.Id}", new { quantity = 1000 });
        var delete = await admin.DeleteAsync($"/api/inventory-transactions/{entry.Id}");

        Assert.False(put.IsSuccessStatusCode);
        Assert.False(delete.IsSuccessStatusCode);
        var after = (await LedgerAsync(admin, part.Id)).Single();
        Assert.Equal(entry.Quantity, after.Quantity);
        Assert.Equal(2m, (await GetPartAsync(admin, part.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task The_database_refuses_to_edit_or_delete_a_ledger_row_even_for_a_privileged_login()
    {
        // The tests connect as sa, so this is the trigger at work, not a missing permission: a code path that
        // forgot BR-09, or a hand-run statement, cannot rewrite stock history either.
        var admin = await _factory.AdminClientAsync();
        var part = await CreateStockedPartAsync(admin, onHand: 2);
        var entry = (await LedgerAsync(admin, part.Id)).Single();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = db.InventoryTransactions.Where(t => t.Id == entry.Id);

        var update = await Assert.ThrowsAsync<SqlException>(() =>
            rows.ExecuteUpdateAsync(s => s.SetProperty(t => t.Quantity, 1000m)));
        var delete = await Assert.ThrowsAsync<SqlException>(() => rows.ExecuteDeleteAsync());
        var tracked = await db.InventoryTransactions.SingleAsync(t => t.Id == entry.Id);
        tracked.Notes = "edited";
        var save = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.All(new[] { update, delete, (SqlException)save.InnerException! }, e => Assert.Equal(50009, e.Number));
        var after = (await LedgerAsync(admin, part.Id)).Single();
        Assert.Equal(entry.Quantity, after.Quantity);
        Assert.Equal(entry.Notes, after.Notes);

        // A statement that matches no ledger row is not a change, so it is let through.
        Assert.Equal(0, await db.InventoryTransactions.Where(t => t.Id == -1).ExecuteDeleteAsync());
    }

    // ---- AC-09 (API): low stock ----

    [Fact]
    public async Task Low_stock_lists_active_parts_at_or_below_their_minimum_and_nothing_else()
    {
        var warehouse = await WarehouseAsync();
        var below = await CreateStockedPartAsync(warehouse, onHand: 1, minimumStockLevel: 5);
        var atMinimum = await CreateStockedPartAsync(warehouse, onHand: 5, minimumStockLevel: 5);
        var above = await CreateStockedPartAsync(warehouse, onHand: 6, minimumStockLevel: 5);
        var inactiveBelow = await CreateStockedPartAsync(warehouse, onHand: 0, minimumStockLevel: 5);
        await warehouse.PutAsJsonAsync($"/api/parts/{inactiveBelow.Id}", EditOf(inactiveBelow, e => e.IsActive = false));

        var lowStock = (await warehouse.GetFromJsonAsync<List<PartDto>>("/api/parts/low-stock"))!;

        Assert.Contains(lowStock, p => p.Id == below.Id);
        Assert.Contains(lowStock, p => p.Id == atMinimum.Id);
        Assert.DoesNotContain(lowStock, p => p.Id == above.Id);
        Assert.DoesNotContain(lowStock, p => p.Id == inactiveBelow.Id);
        Assert.All(lowStock, p => Assert.True(p.IsLowStock));
        // The most urgent (furthest below its minimum) comes first.
        Assert.True(lowStock.FindIndex(p => p.Id == below.Id) < lowStock.FindIndex(p => p.Id == atMinimum.Id));
    }

    [Fact]
    public async Task Receiving_enough_stock_takes_a_part_off_the_low_stock_list()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreateStockedPartAsync(warehouse, onHand: 2, minimumStockLevel: 4);
        Assert.Contains((await warehouse.GetFromJsonAsync<List<PartDto>>("/api/parts/low-stock"))!, p => p.Id == part.Id);

        await ReceiveAsync(warehouse, part.Id, 10);

        Assert.DoesNotContain((await warehouse.GetFromJsonAsync<List<PartDto>>("/api/parts/low-stock"))!, p => p.Id == part.Id);
        Assert.False((await GetPartAsync(warehouse, part.Id)).IsLowStock);
    }

    // ---- Transaction history ----

    [Fact]
    public async Task History_is_newest_first_and_filters_by_part_type_and_date_range()
    {
        var warehouse = await WarehouseAsync();
        var part = await CreatePartAsync(warehouse);
        var other = await CreatePartAsync(warehouse);
        var start = DateTime.UtcNow.AddSeconds(-1);
        await ReceiveAsync(warehouse, part.Id, 10, "first");
        await PostStockMovementAsync(warehouse, part.Id, "Adjustment", -1, "second");
        await ReceiveAsync(warehouse, other.Id, 3, "other part");

        var forPart = await LedgerAsync(warehouse, part.Id);
        var adjustments = (await warehouse.GetFromJsonAsync<List<InventoryTransactionDto>>(
            $"/api/inventory-transactions?partId={part.Id}&type=Adjustment"))!;
        var inWindow = (await warehouse.GetFromJsonAsync<List<InventoryTransactionDto>>(
            $"/api/inventory-transactions?from={start:O}&to={DateTime.UtcNow.AddMinutes(1):O}"))!;
        var beforeWindow = (await warehouse.GetFromJsonAsync<List<InventoryTransactionDto>>(
            $"/api/inventory-transactions?partId={part.Id}&to={start:O}"))!;
        var forWorkOrder = (await warehouse.GetFromJsonAsync<List<InventoryTransactionDto>>(
            $"/api/inventory-transactions?partId={part.Id}&workOrderId=1"))!;

        Assert.Equal(new[] { "second", "first" }, forPart.Select(t => t.Notes));
        Assert.All(forPart, t => Assert.Equal(part.PartNumber, t.PartNumber));
        Assert.Equal("second", Assert.Single(adjustments).Notes);
        Assert.Contains(inWindow, t => t.Notes == "other part");
        Assert.Contains(inWindow, t => t.Notes == "first");
        Assert.Empty(beforeWindow);
        Assert.Empty(forWorkOrder); // this part was never used on a job (WorkOrderPartsTests covers the filter)
    }

    [Theory]
    [InlineData("type=Shrinkage", "Type must be one of")]
    [InlineData("from=2026-09-10T00:00:00Z&to=2026-09-01T00:00:00Z", "before the 'to' date")]
    public async Task Invalid_history_filters_are_a_400(string query, string expected)
    {
        var warehouse = await WarehouseAsync();

        var response = await warehouse.GetAsync($"/api/inventory-transactions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await DetailAsync(response));
    }

    [Fact]
    public async Task Unknown_part_ids_return_404()
    {
        var warehouse = await WarehouseAsync();

        var get = await warehouse.GetAsync($"/api/parts/{int.MaxValue}");
        var put = await warehouse.PutAsJsonAsync($"/api/parts/{int.MaxValue}", new UpdatePartRequest
        {
            PartNumber = "X", Name = "X", Unit = "pcs", RowVersion = Convert.ToBase64String(new byte[8])
        });

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }
}
