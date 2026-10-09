using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.Services.Analytics;

namespace SarawakBizOps.Api.Tests;

/// <summary>The dashboard payload cache, without a database: one computation per view and period while it lasts.</summary>
public class DashboardCacheTests
{
    private static DashboardCache Cache(int seconds)
        => new(new MemoryCache(new MemoryCacheOptions()), Options.Create(new DashboardOptions { CacheSeconds = seconds }));

    [Fact]
    public async Task A_view_and_period_is_computed_once_while_it_is_cached()
    {
        var cache = Cache(60);
        var computed = 0;
        Task<string> Compute() => Task.FromResult($"payload {++computed}");

        Assert.Equal("payload 1", await cache.GetOrComputeAsync("Manager", 6, Compute));
        Assert.Equal("payload 1", await cache.GetOrComputeAsync("Manager", 6, Compute));
        Assert.Equal("payload 2", await cache.GetOrComputeAsync("Manager", 12, Compute));   // another period
        Assert.Equal("payload 3", await cache.GetOrComputeAsync("Warehouse", 6, Compute)); // another view
        Assert.Equal(3, computed);
    }

    [Fact]
    public async Task Zero_seconds_turns_the_cache_off()
    {
        var cache = Cache(0);
        var computed = 0;
        Task<string> Compute() => Task.FromResult($"payload {++computed}");

        await cache.GetOrComputeAsync("Manager", 6, Compute);
        await cache.GetOrComputeAsync("Manager", 6, Compute);

        Assert.Equal(2, computed);
    }

    [Fact]
    public async Task A_failed_computation_is_not_kept()
    {
        var cache = Cache(60);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => cache.GetOrComputeAsync<string>("Manager", 6, () => throw new OperationCanceledException()));

        Assert.Equal("fresh", await cache.GetOrComputeAsync("Manager", 6, () => Task.FromResult("fresh")));
    }

    [Fact]
    public void A_negative_lifetime_is_refused_at_startup()
        => Assert.Throws<InvalidOperationException>(() => Cache(-1));
}
