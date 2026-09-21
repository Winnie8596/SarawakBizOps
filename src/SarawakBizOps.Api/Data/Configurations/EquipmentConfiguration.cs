using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.Property(e => e.SerialNumber).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.SerialNumber).IsUnique();

        builder.Property(e => e.EquipmentType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Brand).HasMaxLength(100);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasMany(e => e.ServiceRequests)
            .WithOne(sr => sr.Equipment)
            .HasForeignKey(sr => sr.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
