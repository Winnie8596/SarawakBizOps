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
