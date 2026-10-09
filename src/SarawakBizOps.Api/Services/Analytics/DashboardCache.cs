using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Analytics;

/// <summary>The "Dashboard" settings for <see cref="DashboardCache"/>.</summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>How long a computed dashboard is served before it is computed again; 0 turns the cache off.</summary>
    public int CacheSeconds { get; set; } = 15;
}

/// <summary>
/// Keeps each dashboard payload for a few seconds. The numbers are the same for everyone who gets a view (none is
/// per-user), so a busy office refreshing the page costs one set of queries per view, period and interval rather
/// than one per request. A payload can be up to <see cref="DashboardOptions.CacheSeconds"/> old; its
/// <c>GeneratedAt</c> says when it was computed. A failed or cancelled computation is not kept.
/// </summary>
public sealed class DashboardCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _lifetime;

    public DashboardCache(IMemoryCache cache, IOptions<DashboardOptions> options)
    {
        if (options.Value.CacheSeconds < 0)
        {
            throw new InvalidOperationException($"{DashboardOptions.SectionName}:CacheSeconds cannot be negative.");
        }

        _cache = cache;
        _lifetime = TimeSpan.FromSeconds(options.Value.CacheSeconds);
    }

    public async Task<T> GetOrComputeAsync<T>(string view, int months, Func<Task<T>> compute) where T : class
    {
        if (_lifetime == TimeSpan.Zero)
        {
            return await compute();
        }

        return (await _cache.GetOrCreateAsync(("dashboard", view, months), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _lifetime;
            return compute();
        }))!;
    }
}
