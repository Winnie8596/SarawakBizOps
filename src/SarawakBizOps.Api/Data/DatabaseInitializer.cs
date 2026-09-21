using Microsoft.EntityFrameworkCore;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// Startup database work: optionally apply migrations (Database:MigrateOnStartup, off by default and
/// switched on only by docker-compose), then run the seeders. Runs before Kestrel starts listening,
/// so once the API answers at all, the schema and seed data are in place.
/// </summary>
public static class DatabaseInitializer
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            await MigrateWithRetryAsync(services.GetRequiredService<ApplicationDbContext>(), logger, ct);
        }

        await DbSeeder.SeedAsync(services);

        if (DemoSeeder.IsEnabled(configuration))
        {
            await DemoSeeder.SeedAsync(services, ct);
            logger.LogInformation("Demo data seeded (Seed:Demo is enabled).");
        }
    }

    // SQL Server in a container needs a while to accept connections, even after it reports started,
    // so a few connection failures at boot are expected rather than fatal.
    private static async Task MigrateWithRetryAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Database migrations applied.");
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}); retrying in {Delay}s.",
                    attempt, MaxAttempts, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }
}
