using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class ServiceReportConfiguration : IEntityTypeConfiguration<ServiceReport>
{
    public void Configure(EntityTypeBuilder<ServiceReport> builder)
    {
        builder.Property(r => r.ReportNumber).IsRequired().HasMaxLength(60);
        builder.HasIndex(r => r.ReportNumber).IsUnique();
        builder.Property(r => r.FileUrl).HasMaxLength(500);
    }
}
