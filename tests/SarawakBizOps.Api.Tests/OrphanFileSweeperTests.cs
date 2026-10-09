using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Storage;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// 6.11: the scheduled cleanup of stored files no row points at, run against the real database and Azurite.
/// Each test sweeps only its own job's folder, so it cannot touch another test's files.
/// </summary>
[Collection(ApiCollection.Name)]
public class OrphanFileSweeperTests
{
    private readonly ApiFactory _factory;

    public OrphanFileSweeperTests(ApiFactory factory) => _factory = factory;

    private static async Task<List<string>> KeysAsync(IFileStorage storage, string prefix)
    {
        var keys = new List<string>();
        await foreach (var file in storage.ListAsync(prefix, CancellationToken.None))
        {
            keys.Add(file.Key);
        }
        return keys;
    }

    [Fact]
    public async Task Files_no_row_points_at_are_deleted_once_old_enough_and_the_jobs_own_files_are_kept()
    {
        // A job with every kind of file the API stores: a signature, a photo and its service report.
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var job = await CreateReadyToCompleteWorkOrderAsync(admin, manager, technician, user.Id);
        var photo = (await UploadPhotoAsync(technician, job.Id)).Photos.Single();
        await CompleteWorkOrderAsync(technician, job.Id);
        await GenerateReportAsync(manager, job.Id);

        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        var folder = $"work-orders/{job.Id}/";
        var kept = await KeysAsync(storage, folder);
        Assert.Equal(3, kept.Count);

        // What a lost commit or a failed cleanup leaves behind.
        var orphans = new[] { $"{folder}photo-{Guid.NewGuid():N}.jpg", $"{folder}report-{Guid.NewGuid():N}.pdf" };
        foreach (var key in orphans)
        {
            await storage.SaveAsync(key, new MemoryStream(PhotoJpeg), "image/jpeg", CancellationToken.None);
        }

        using var scope = _factory.Services.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<OrphanFileSweeper>();

        // Not old enough yet: as far as the sweep can tell, an upload could still be committing its row.
        var early = await sweeper.SweepAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None, folder);
        Assert.Equal(5, early.Scanned);
        Assert.Empty(early.Deleted);

        // Old enough (the cutoff is moved forward instead of waiting a day; a margin covers the emulator's clock).
        var swept = await sweeper.SweepAsync(DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None, folder);

        Assert.Equal(orphans.Order(), swept.Deleted.Order());
        Assert.Equal(kept.Order(), (await KeysAsync(storage, folder)).Order());
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/work-orders/{job.Id}/photos/{photo.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/work-orders/{job.Id}/signature")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/work-orders/{job.Id}/report")).StatusCode);

        // A second sweep finds nothing more to do.
        Assert.Empty((await sweeper.SweepAsync(DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None, folder)).Deleted);
    }

    [Fact]
    public async Task Only_job_files_are_ever_swept()
    {
        using var scope = _factory.Services.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<OrphanFileSweeper>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sweeper.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None, prefix: "contract/"));
    }

    [Theory]
    [InlineData("00:59:59", "1.00:00:00")]
    [InlineData("1.00:00:00", "00:00:00")]
    public void The_cleanup_refuses_to_start_with_a_minimum_age_under_an_hour_or_no_interval(string minimumAge, string interval)
    {
        var options = Options.Create(new OrphanFileCleanupOptions
        {
            MinimumAge = TimeSpan.Parse(minimumAge),
            Interval = TimeSpan.Parse(interval)
        });

        Assert.Throws<InvalidOperationException>(() => new OrphanFileCleanupService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), options, TimeProvider.System,
            NullLogger<OrphanFileCleanupService>.Instance));
    }

    [Fact]
    public void The_cleanup_runs_in_the_api_by_default()
    {
        Assert.Contains(_factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            s => s is OrphanFileCleanupService);
        Assert.True(_factory.Services.GetRequiredService<IOptions<OrphanFileCleanupOptions>>().Value.Enabled);
    }
}
