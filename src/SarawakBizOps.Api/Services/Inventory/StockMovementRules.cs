using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Inventory;

/// <summary>
/// The pure half of the inventory accounting rule (SDD §10, BR-09), testable without a database:
/// which movement types the warehouse may raise directly, and what each one needs to be valid.
/// The other half — "stock never goes below zero, and the ledger row and the stock change commit
/// together" — needs the database and lives in <see cref="InventoryService"/>.
/// </summary>
public static class StockMovementRules
{
    /// <summary>Issue and Return exist only as the side effect of a work-order part operation (Phase 7).</summary>
    public static bool IsDirectlyCreatable(InventoryTransactionType type)
        => type is InventoryTransactionType.Receive or InventoryTransactionType.Adjustment;

    /// <summary>Returns the reason a movement is invalid, or null when it may go ahead.</summary>
    public static string? Validate(InventoryTransactionType type, decimal quantity, string? notes)
    {
        if (!IsDirectlyCreatable(type))
        {
            return $"{type} transactions are created only by adding or returning parts on a work order.";
        }

        if (!Quantities.HasAtMostDecimals(quantity, Quantities.Scale))
        {
            return $"Quantity can have at most {Quantities.Scale} decimal places.";
        }

        if (Math.Abs(quantity) > Quantities.Max)
        {
            return "Quantity is too large.";
        }

        return type switch
        {
            InventoryTransactionType.Receive when quantity <= 0
                => "A delivery must receive a quantity greater than zero.",
            InventoryTransactionType.Adjustment when quantity == 0
                => "An adjustment must change the stock: enter a positive or negative quantity.",
            // Corrections are the audit trail (BR-09): an adjustment without a reason explains nothing.
            InventoryTransactionType.Adjustment when string.IsNullOrWhiteSpace(notes)
                => "A reason is required for a stock adjustment.",
            _ => null
        };
    }

    /// <summary>Whether applying <paramref name="quantity"/> leaves a non-negative balance (BR-04).</summary>
    public static bool KeepsStockNonNegative(decimal quantityOnHand, decimal quantity) => quantityOnHand + quantity >= 0;
}
