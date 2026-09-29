using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class ServiceReportConfiguration : IEntityTypeConfiguration<ServiceReport>
{
    /// <summary>The SQL Server SEQUENCE report numbers are drawn from (Phase 10): unique under parallel
    /// generation without locking any table.</summary>
    public const string NumberSequence = "ServiceReportNumbers";

    public void Configure(EntityTypeBuilder<ServiceReport> builder)
    {
        builder.Property(r => r.ReportNumber).IsRequired().HasMaxLength(60);
        builder.HasIndex(r => r.ReportNumber).IsUnique();
        builder.Property(r => r.BlobKey).IsRequired().HasMaxLength(200);

        // The Reports page lists newest first.
        builder.HasIndex(r => r.GeneratedAt);

        builder.HasOne(r => r.GeneratedByUser)
            .WithMany()
            .HasForeignKey(r => r.GeneratedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
