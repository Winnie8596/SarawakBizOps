using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data.Configurations;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// IdentityDbContext gives us AspNetUsers/AspNetRoles/etc. for free.
/// Business tables are added as normal DbSets alongside them.
/// Every DateTime is UTC (written from DateTime.UtcNow or TimeProvider.GetUtcNow), except
/// Equipment.InstallationDate, which is a calendar date. SQL Server's datetime2 keeps no offset, so values
/// come back with DateTimeKind.Unspecified; the web app reads the API's timestamps as UTC accordingly.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<WorkOrderPhoto> WorkOrderPhotos => Set<WorkOrderPhoto>();
    public DbSet<ServiceReport> ServiceReports => Set<ServiceReport>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AiSourceDocument> AiSourceDocuments => Set<AiSourceDocument>();
    public DbSet<AiSourceDocumentModel> AiSourceDocumentModels => Set<AiSourceDocumentModel>();
    public DbSet<AiDocumentChunk> AiDocumentChunks => Set<AiDocumentChunk>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must run first — it builds the Identity tables this model extends.
        base.OnModelCreating(builder);

        // Every IEntityTypeConfiguration in Data/Configurations, so a new one cannot be forgotten here.
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.HasSequence<long>(ServiceReportConfiguration.NumberSequence).StartsAt(1).IncrementsBy(1);
    }
}
