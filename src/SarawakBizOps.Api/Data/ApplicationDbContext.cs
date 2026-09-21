using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data.Configurations;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// IdentityDbContext gives us AspNetUsers/AspNetRoles/etc. for free.
/// Business tables are added as normal DbSets alongside them.
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must run first — it builds the Identity tables this model extends.
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new CustomerConfiguration());
        builder.ApplyConfiguration(new EquipmentConfiguration());
        builder.ApplyConfiguration(new ServiceRequestConfiguration());
        builder.ApplyConfiguration(new WorkOrderConfiguration());
        builder.ApplyConfiguration(new PartConfiguration());
        builder.ApplyConfiguration(new WorkOrderPartConfiguration());
        builder.ApplyConfiguration(new InventoryTransactionConfiguration());
        builder.ApplyConfiguration(new WorkOrderPhotoConfiguration());
        builder.ApplyConfiguration(new ServiceReportConfiguration());
    }
}
