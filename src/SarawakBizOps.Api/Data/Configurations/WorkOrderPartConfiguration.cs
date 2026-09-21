using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class WorkOrderPartConfiguration : IEntityTypeConfiguration<WorkOrderPart>
{
    public void Configure(EntityTypeBuilder<WorkOrderPart> builder)
    {
        builder.Property(wp => wp.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(wp => wp.UnitPrice).HasColumnType("decimal(18,2)");

        builder.HasOne(wp => wp.WorkOrder)
            .WithMany(wo => wo.PartsUsed)
            .HasForeignKey(wp => wp.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(wp => wp.Part)
            .WithMany(p => p.WorkOrderParts)
            .HasForeignKey(wp => wp.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
