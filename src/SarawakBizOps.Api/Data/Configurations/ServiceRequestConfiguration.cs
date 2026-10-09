using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.Property(sr => sr.ProblemDescription).IsRequired().HasMaxLength(2000);
        builder.Property(sr => sr.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(sr => sr.Status).HasConversion<string>().HasMaxLength(20);

        // FK to Identity's ApplicationUser (string key) — see design doc Section 5.
        builder.HasOne(sr => sr.CreatedByUser)
            .WithMany()
            .HasForeignKey(sr => sr.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sr => sr.ApprovedByUser)
            .WithMany()
            .HasForeignKey(sr => sr.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(sr => sr.RejectionReason).HasMaxLength(500);

        builder.HasOne(sr => sr.RejectedByUser)
            .WithMany()
            .HasForeignKey(sr => sr.RejectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(sr => sr.CancellationReason).HasMaxLength(500);

        builder.HasOne(sr => sr.CancelledByUser)
            .WithMany()
            .HasForeignKey(sr => sr.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The Manager queue and every list filter start from the status.
        builder.HasIndex(sr => sr.Status);

        // One-to-many: cancelled jobs stay with their request. BR-12's "one live job" is the filtered unique index
        // in WorkOrderConfiguration.
        builder.HasMany(sr => sr.WorkOrders)
            .WithOne(wo => wo.ServiceRequest)
            .HasForeignKey(wo => wo.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
