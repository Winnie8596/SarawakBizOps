using System.Globalization;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Inventory;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Pure unit tests (no database) for the warehouse's half of the inventory accounting rule (SDD §10):
/// which movements may be raised directly and what each needs. The database half — atomicity and
/// never-negative stock under concurrency — is proven by <see cref="InventoryTests"/>.
/// </summary>
public class StockMovementRulesTests
{
    // InlineData cannot hold decimals, so quantities are written as invariant-culture strings.
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(InventoryTransactionType.Receive, true)]
    [InlineData(InventoryTransactionType.Adjustment, true)]
    [InlineData(InventoryTransactionType.Issue, false)]
    [InlineData(InventoryTransactionType.Return, false)]
    public void Only_receive_and_adjustment_can_be_raised_directly(InventoryTransactionType type, bool expected)
    {
        Assert.Equal(expected, StockMovementRules.IsDirectlyCreatable(type));
    }

    [Theory]
    [InlineData(InventoryTransactionType.Issue)]
    [InlineData(InventoryTransactionType.Return)]
    public void Issue_and_return_are_refused_and_the_message_says_where_they_come_from(InventoryTransactionType type)
    {
        var refusal = StockMovementRules.Validate(type, 1, "some note");

        Assert.NotNull(refusal);
        Assert.Contains("work order", refusal);
    }

    [Theory]
    [InlineData("10", null)]
    [InlineData("0.5", null)]
    [InlineData("2.125", null)]
    public void A_positive_receive_is_valid_with_or_without_a_note(string quantity, string? notes)
    {
        Assert.Null(StockMovementRules.Validate(InventoryTransactionType.Receive, D(quantity), notes));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_receive_must_add_stock(string quantity)
    {
        Assert.NotNull(StockMovementRules.Validate(InventoryTransactionType.Receive, D(quantity), null));
    }

    [Theory]
    [InlineData("-3.5")]
    [InlineData("4")]
    public void An_adjustment_in_either_direction_is_valid_with_a_reason(string quantity)
    {
        Assert.Null(StockMovementRules.Validate(InventoryTransactionType.Adjustment, D(quantity), "Stocktake correction"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_adjustment_without_a_reason_is_refused(string? notes)
    {
        var refusal = StockMovementRules.Validate(InventoryTransactionType.Adjustment, -1, notes);

        Assert.Equal("A reason is required for a stock adjustment.", refusal);
    }

    [Fact]
    public void A_zero_adjustment_is_refused()
    {
        Assert.NotNull(StockMovementRules.Validate(InventoryTransactionType.Adjustment, 0, "Nothing changed"));
    }

    [Theory]
    [InlineData(InventoryTransactionType.Receive, "1.2345")]
    [InlineData(InventoryTransactionType.Adjustment, "-0.0001")]
    public void More_than_three_decimal_places_are_refused_rather_than_silently_rounded(
        InventoryTransactionType type, string quantity)
    {
        var refusal = StockMovementRules.Validate(type, D(quantity), "note");

        Assert.NotNull(refusal);
        Assert.Contains("decimal places", refusal);
    }

    [Theory]
    [InlineData("5", "-5", true)]
    [InlineData("5", "-5.001", false)]
    [InlineData("0", "3", true)]
    [InlineData("2.5", "-2.4", true)]
    public void Stock_may_reach_zero_but_never_go_below_it(string onHand, string quantity, bool expected)
    {
        Assert.Equal(expected, StockMovementRules.KeepsStockNonNegative(D(onHand), D(quantity)));
    }
}
