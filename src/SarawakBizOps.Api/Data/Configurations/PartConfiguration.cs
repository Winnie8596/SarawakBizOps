using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.Property(p => p.PartNumber).IsRequired().HasMaxLength(60);
        builder.HasIndex(p => p.PartNumber).IsUnique();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Unit).IsRequired().HasMaxLength(20);

        builder.Property(p => p.UnitCost).HasColumnType("decimal(18,2)");
        builder.Property(p => p.QuantityOnHand).HasColumnType("decimal(18,3)");
        builder.Property(p => p.MinimumStockLevel).HasColumnType("decimal(18,3)");

        // Backstop for BR-04: the inventory service already refuses any movement that would take stock
        // below zero with a conditional UPDATE, but the database refuses it too if a code path ever forgets.
        builder.ToTable(t => t.HasCheckConstraint("CK_Parts_QuantityOnHand_NonNegative", "[QuantityOnHand] >= 0"));
    }
}
