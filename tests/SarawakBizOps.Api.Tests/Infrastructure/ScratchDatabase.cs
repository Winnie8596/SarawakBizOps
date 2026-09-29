using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// A brand-new, migrated database on the shared SQL Server container, dropped again on dispose. The analytics
/// operations count across whole tables, so their known-answer tests (Phase 11) need a dataset nobody else
/// writes to: this one holds exactly the rows a test inserts, directly through EF, with the timestamps it chooses.
/// </summary>
public sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    private ScratchDatabase(DbContextOptions<ApplicationDbContext> options) => _options = options;

    public string UserId { get; private set; } = string.Empty;
    public int CustomerId { get; private set; }
    public int EquipmentId { get; private set; }

    /// <summary>Creates and migrates the database, with one user, customer and equipment to hang rows on.</summary>
    public static async Task<ScratchDatabase> CreateAsync(ApiFactory factory)
    {
        var connection = new SqlConnectionStringBuilder(factory.SqlConnectionString)
        {
            InitialCatalog = $"scratch_{Guid.NewGuid():N}"
        };
        var scratch = new ScratchDatabase(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options);

        await using var db = scratch.NewContext();
        await db.Database.MigrateAsync();

        var user = new ApplicationUser { UserName = "analyst@test.local", Email = "analyst@test.local", FullName = "Test Analyst" };
        var customer = new Customer { CompanyName = "Scratch Customer" };
        var equipment = new Equipment { Customer = customer, SerialNumber = "SCR-001", EquipmentType = "Pump" };
        db.Users.Add(user);
        db.Equipment.Add(equipment);
        await db.SaveChangesAsync();

        (scratch.UserId, scratch.CustomerId, scratch.EquipmentId) = (user.Id, customer.Id, equipment.Id);
        return scratch;
    }

    public ApplicationDbContext NewContext() => new(_options);

    public async Task AddRequestsAsync(params (RequestPriority Priority, ServiceRequestStatus Status)[] requests)
    {
        await using var db = NewContext();
        db.ServiceRequests.AddRange(requests.Select(r => NewRequest(r.Priority, r.Status)));
        await db.SaveChangesAsync();
    }

    /// <summary>Each job gets its own (Assigned) request, as BR-12 requires.</summary>
    public async Task AddWorkOrdersAsync(params (WorkOrderStatus Status, DateTime? CompletedAtUtc)[] jobs)
    {
        await using var db = NewContext();
        foreach (var (status, completedAt) in jobs)
        {
            db.WorkOrders.Add(new WorkOrder
            {
                ServiceRequest = NewRequest(RequestPriority.Medium, ServiceRequestStatus.Assigned),
                TechnicianId = UserId,
                Status = status,
                AssignedAt = DateTime.UtcNow,
                CompletedAt = completedAt
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task AddPartsAsync(params (string PartNumber, decimal OnHand, decimal Minimum, bool IsActive)[] parts)
    {
        await using var db = NewContext();
        db.Parts.AddRange(parts.Select(p => new Part
        {
            PartNumber = p.PartNumber,
            Name = $"Part {p.PartNumber}",
            QuantityOnHand = p.OnHand,
            MinimumStockLevel = p.Minimum,
            IsActive = p.IsActive
        }));
        await db.SaveChangesAsync();
    }

    private ServiceRequest NewRequest(RequestPriority priority, ServiceRequestStatus status) => new()
    {
        CustomerId = CustomerId,
        EquipmentId = EquipmentId,
        ProblemDescription = "Scratch problem",
        Priority = priority,
        Status = status,
        CreatedByUserId = UserId
    };

    public async ValueTask DisposeAsync()
    {
        await using var db = NewContext();
        // Drop our own connections first (Microsoft.Data.SqlClient pools them), or DROP DATABASE would wait.
        SqlConnection.ClearAllPools();
        await db.Database.EnsureDeletedAsync();
    }
}

/// <summary>A clock stopped at one instant, so "this month" is whatever a test says it is.</summary>
public sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedClock(DateTime utcNow) => _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
