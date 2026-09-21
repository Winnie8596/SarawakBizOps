using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.Property(t => t.TransactionType).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(t => t.Notes).HasMaxLength(500);

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

        // BR-09: rows are immutable. That's an application-layer rule (no
        // Update/Delete exposed by the inventory service) — the schema alone
        // can't enforce "never edited", so don't rely on it as a substitute.
    }
}
