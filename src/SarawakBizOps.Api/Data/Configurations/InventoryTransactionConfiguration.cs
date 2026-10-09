using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public const string ImmutabilityTrigger = "TR_InventoryTransactions_Immutable";

    /// <summary>
    /// The ledger lives in its own schema so that SQL permissions can be set on it alone:
    /// scripts/sql/create-logins.sql grants the app login SELECT and INSERT here and DENYs UPDATE and DELETE.
    /// </summary>
    public const string Schema = "ledger";

    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.Property(t => t.TransactionType).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(t => t.Notes).HasMaxLength(500);

        // History filters (Phase 6): "this part's movements, newest first" and date-range scans across
        // all parts. The composite index also serves the PartId foreign key, replacing its own index.
        builder.HasIndex(t => new { t.PartId, t.CreatedAt });
        builder.HasIndex(t => t.CreatedAt);

        builder.HasOne(t => t.Part)
            .WithMany(p => p.Transactions)
            .HasForeignKey(t => t.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.WorkOrder)
            .WithMany()
            .HasForeignKey(t => t.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // BR-09: rows are immutable, enforced three times over. The inventory service exposes no update or delete;
        // the app's own login has no UPDATE or DELETE permission on the ledger schema; and this trigger (created
        // in the LedgerImmutability migration) refuses every UPDATE and DELETE, whoever sends it, sa included.
        // Declaring the trigger makes EF Core save without an OUTPUT clause, which SQL Server does not allow on a
        // table with triggers.
        builder.ToTable("InventoryTransactions", Schema, t => t.HasTrigger(ImmutabilityTrigger));
    }
}
