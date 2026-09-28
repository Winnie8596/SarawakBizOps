using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// The inventory invariant (BR-09, SDD §10): for every part, the cached QuantityOnHand equals the sum of
/// its ledger rows. Read straight from the database, not through the API, so it checks what is really stored.
/// </summary>
public static class LedgerReconciliation
{
    public record Mismatch(string PartNumber, decimal QuantityOnHand, decimal LedgerTotal);

    public static async Task<IReadOnlyList<Mismatch>> MismatchesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.Parts.AsNoTracking()
            .Select(p => new
            {
                p.PartNumber,
                p.QuantityOnHand,
                LedgerTotal = p.Transactions.Sum(t => (decimal?)t.Quantity) ?? 0m
            })
            .ToListAsync();

        return rows
            .Where(r => r.QuantityOnHand != r.LedgerTotal)
            .Select(r => new Mismatch(r.PartNumber, r.QuantityOnHand, r.LedgerTotal))
            .ToList();
    }

    /// <summary>Fails the test, naming every part whose stock has drifted from its ledger.</summary>
    public static async Task AssertReconcilesAsync(IServiceProvider services)
    {
        var mismatches = await MismatchesAsync(services);
        Assert.True(mismatches.Count == 0,
            "Stock does not reconcile with the ledger: " +
            string.Join("; ", mismatches.Select(m => $"{m.PartNumber} on hand {m.QuantityOnHand}, ledger {m.LedgerTotal}")));
    }
}
