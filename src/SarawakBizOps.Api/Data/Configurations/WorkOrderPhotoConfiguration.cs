using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class WorkOrderPhotoConfiguration : IEntityTypeConfiguration<WorkOrderPhoto>
{
    public void Configure(EntityTypeBuilder<WorkOrderPhoto> builder)
    {
        builder.Property(p => p.FileUrl).IsRequired().HasMaxLength(500);
        builder.Property(p => p.Description).HasMaxLength(300);

        builder.HasOne(p => p.WorkOrder)
            .WithMany(wo => wo.Photos)
            .HasForeignKey(p => p.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.UploadedByUser)
            .WithMany()
            .HasForeignKey(p => p.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
