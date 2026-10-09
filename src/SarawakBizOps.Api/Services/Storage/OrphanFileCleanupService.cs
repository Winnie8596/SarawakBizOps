using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Storage;

/// <summary>The "Storage:OrphanCleanup" settings for <see cref="OrphanFileCleanupService"/>.</summary>
public sealed class OrphanFileCleanupOptions
{
    public const string SectionName = "Storage:OrphanCleanup";

    /// <summary>Below this, a slow upload's file could be deleted before its row is committed.</summary>
    public static readonly TimeSpan SmallestMinimumAge = TimeSpan.FromHours(1);

    public bool Enabled { get; set; } = true;

    /// <summary>Wait after startup before the first sweep, so it never competes with migrations and seeding.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>A file must be at least this old to be deleted (and at least <see cref="SmallestMinimumAge"/>).</summary>
    public TimeSpan MinimumAge { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>
/// Runs <see cref="OrphanFileSweeper"/> on a timer inside the API (6.11): shortly after startup, then every
/// <see cref="OrphanFileCleanupOptions.Interval"/>. A failed sweep (storage or database unreachable) is logged and
/// tried again at the next tick; it never stops the API.
/// </summary>
public sealed class OrphanFileCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OrphanFileCleanupOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OrphanFileCleanupService> _logger;

    public OrphanFileCleanupService(
        IServiceScopeFactory scopes, IOptions<OrphanFileCleanupOptions> options, TimeProvider time,
        ILogger<OrphanFileCleanupService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;

        if (_options.MinimumAge < OrphanFileCleanupOptions.SmallestMinimumAge)
        {
            throw new InvalidOperationException(
                $"{OrphanFileCleanupOptions.SectionName}:MinimumAge must be at least {OrphanFileCleanupOptions.SmallestMinimumAge}, " +
                "or a file still being uploaded could be deleted.");
        }

        if (_options.Interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{OrphanFileCleanupOptions.SectionName}:Interval must be positive.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(_options.InitialDelay, _time, stoppingToken);
            using var timer = new PeriodicTimer(_options.Interval, _time);
            do
            {
                await SweepOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sweeper = scope.ServiceProvider.GetRequiredService<OrphanFileSweeper>();
            var result = await sweeper.SweepAsync(_time.GetUtcNow() - _options.MinimumAge, ct);
            _logger.LogInformation("Orphaned file sweep: {Scanned} files checked, {Deleted} deleted",
                result.Scanned, result.Deleted.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Orphaned file sweep failed; it will run again in {Interval}", _options.Interval);
        }
    }
}
