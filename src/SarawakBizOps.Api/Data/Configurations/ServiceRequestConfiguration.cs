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

        // The Manager queue and every list filter start from the status.
        builder.HasIndex(sr => sr.Status);

        // 1:1 with WorkOrder — BR-12 (at most one WorkOrder per ServiceRequest).
        builder.HasOne(sr => sr.WorkOrder)
            .WithOne(wo => wo.ServiceRequest)
            .HasForeignKey<WorkOrder>(wo => wo.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
