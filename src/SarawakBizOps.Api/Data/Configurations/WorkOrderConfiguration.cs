using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.Property(wo => wo.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(wo => wo.Diagnosis).HasMaxLength(4000);
        builder.Property(wo => wo.WorkPerformed).HasMaxLength(4000);
        builder.Property(wo => wo.Notes).HasMaxLength(2000);
        builder.Property(wo => wo.SignatureBlobKey).HasMaxLength(200);
        builder.Property(wo => wo.CancellationReason).HasMaxLength(500);
        builder.Property(wo => wo.PendingPartsReason).HasMaxLength(500);
        builder.Property(wo => wo.PendingPartQuantity).HasColumnType("decimal(18,3)");

        // Phase 11 dashboard (SDD §14): WorkOrdersByStatus is an index-only count over Status, and
        // JobsCompletedPerMonth a range seek on CompletedAt that reads Status from the index to keep only
        // Completed/Approved jobs. Both also serve the work-order list's status filter and future date filters.
        builder.HasIndex(wo => wo.Status);
        builder.HasIndex(wo => wo.CompletedAt).IncludeProperties(wo => wo.Status);

        builder.HasOne(wo => wo.PendingPart)
            .WithMany()
            .HasForeignKey(wo => wo.PendingPartId)
            .OnDelete(DeleteBehavior.Restrict);

        // BR-12: a ServiceRequest can produce at most one WorkOrder — but a Cancelled work order
        // (Phase 5 decision) is excluded, so a request whose job was cancelled can be assigned again.
        builder.HasIndex(wo => wo.ServiceRequestId)
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled'");

        // BR-11: TechnicianId must reference a user with the Technician role.
        // EF Core cannot express "role must be X" as a constraint — that check
        // belongs in the assignment service, not the schema.
        builder.HasOne(wo => wo.Technician)
            .WithMany()
            .HasForeignKey(wo => wo.TechnicianId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(wo => wo.ApprovedByUser)
            .WithMany()
            .HasForeignKey(wo => wo.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(wo => wo.CancelledByUser)
            .WithMany()
            .HasForeignKey(wo => wo.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(wo => wo.ServiceReport)
            .WithOne(sr => sr.WorkOrder)
            .HasForeignKey<ServiceReport>(sr => sr.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
