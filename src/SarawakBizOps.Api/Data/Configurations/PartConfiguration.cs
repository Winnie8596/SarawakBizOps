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
        builder.Property(p => p.Unit).IsRequired().HasMaxLength(20);

        builder.Property(p => p.UnitCost).HasColumnType("decimal(18,2)");
        builder.Property(p => p.QuantityOnHand).HasColumnType("decimal(18,3)");
        builder.Property(p => p.MinimumStockLevel).HasColumnType("decimal(18,3)");
    }
}
