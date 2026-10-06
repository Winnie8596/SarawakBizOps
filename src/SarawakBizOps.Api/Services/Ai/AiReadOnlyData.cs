using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The only database access the AI code paths get (PRD §6.10: AI never mutates business state). It exposes queries
/// and nothing else — no SaveChanges, no DbSet — over a context of its own that tracks nothing, and whose
/// <see cref="ReadOnlyGuard"/> refuses SaveChanges and every command that is not a SELECT, so even an
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> on one of these queries fails before it reaches SQL Server.
/// </summary>
public sealed class AiReadOnlyData : IDisposable, IAsyncDisposable
{
    private readonly ApplicationDbContext _db;

    public AiReadOnlyData(AiReadOnlyDataOptions options, TimeProvider clock)
    {
        _db = new ApplicationDbContext(options.DbOptions);
        // Phase 14: the Manager AI runs the dashboard's own named operations, over this guarded context.
        Analytics = new AnalyticsService(_db, clock);
    }

    public IQueryable<WorkOrder> WorkOrders => _db.WorkOrders.AsNoTracking();
    public IQueryable<Part> Parts => _db.Parts.AsNoTracking();
    public IAnalyticsService Analytics { get; }

    public void Dispose() => _db.Dispose();
    public ValueTask DisposeAsync() => _db.DisposeAsync();
}

/// <summary>Built once per host (a singleton), so every scope's read-only context shares one EF service provider.</summary>
public sealed class AiReadOnlyDataOptions
{
    /// <summary>Optional connection string for a login that can only read (db_datareader).</summary>
    public const string ConnectionStringName = "AiReadOnly";

    public DbContextOptions<ApplicationDbContext> DbOptions { get; }

    public AiReadOnlyDataOptions(string connectionString)
    {
        DbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(new ReadOnlyGuard())
            .Options;
    }
}

/// <summary>Refuses any write through the context it is attached to.</summary>
public sealed class ReadOnlyGuard : DbCommandInterceptor, ISaveChangesInterceptor
{
    public const string Message = "The AI data access is read-only.";

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        => throw new InvalidOperationException(Message);

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(Message);

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        => throw new InvalidOperationException(Message);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(Message);

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        EnsureSelect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        EnsureSelect(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        EnsureSelect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        EnsureSelect(command);
        return ValueTask.FromResult(result);
    }

    // EF Core's SQL Server queries (including split queries and Any/Count) all begin with SELECT.
    private static void EnsureSelect(DbCommand command)
    {
        if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Message);
        }
    }
}
