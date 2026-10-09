using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;

namespace SarawakBizOps.Api.Services.Storage;

/// <summary>
/// Deletes stored files that no database row points at (6.11). Every upload writes the file first and the row
/// second, and every replacement or removal deletes the old file only after the row has moved on, so a crash, a
/// lost commit or a failed cleanup can leave a file behind — never a row without its file. This finds those
/// files: a photo, signature or report under <see cref="Prefix"/> that no <c>WorkOrderPhotos.BlobKey</c>,
/// <c>WorkOrders.SignatureBlobKey</c> or <c>ServiceReports.BlobKey</c> names.
/// <para>
/// Only files older than the caller's cutoff are considered. An upload in flight has its file stored but its row
/// not yet committed; the cutoff (a day, by default) is far longer than any request, so such a file is never
/// mistaken for an orphan. Deleting is idempotent, so two API instances sweeping at once do no harm.
/// </para>
/// </summary>
public sealed class OrphanFileSweeper
{
    /// <summary>Where every job file lives (see PhotoRules, SignatureRules and ReportRules.NewKey).</summary>
    public const string Prefix = "work-orders/";

    // Keys are checked against the database this many at a time, which keeps each query's IN list modest.
    private const int BatchSize = 500;

    private readonly ApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<OrphanFileSweeper> _logger;

    public OrphanFileSweeper(ApplicationDbContext db, IFileStorage storage, ILogger<OrphanFileSweeper> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Deletes every unreferenced file under <paramref name="prefix"/> last written before
    /// <paramref name="writtenBefore"/>. <paramref name="prefix"/> must lie within <see cref="Prefix"/>.
    /// </summary>
    public async Task<OrphanSweepResult> SweepAsync(DateTimeOffset writtenBefore, CancellationToken ct, string prefix = Prefix)
    {
        if (!prefix.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Only files under '{Prefix}' are swept.", nameof(prefix));
        }

        var scanned = 0;
        var deleted = new List<string>();
        var batch = new List<string>(BatchSize);
        await foreach (var file in _storage.ListAsync(prefix, ct))
        {
            scanned++;
            if (file.LastModified >= writtenBefore)
            {
                continue;
            }

            batch.Add(file.Key);
            if (batch.Count == BatchSize)
            {
                deleted.AddRange(await DeleteUnreferencedAsync(batch, ct));
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            deleted.AddRange(await DeleteUnreferencedAsync(batch, ct));
        }

        return new OrphanSweepResult(scanned, deleted);
    }

    private async Task<List<string>> DeleteUnreferencedAsync(List<string> keys, CancellationToken ct)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        referenced.UnionWith(await _db.WorkOrderPhotos.AsNoTracking()
            .Where(p => keys.Contains(p.BlobKey)).Select(p => p.BlobKey).ToListAsync(ct));
        referenced.UnionWith(await _db.WorkOrders.AsNoTracking()
            .Where(w => w.SignatureBlobKey != null && keys.Contains(w.SignatureBlobKey)).Select(w => w.SignatureBlobKey!).ToListAsync(ct));
        referenced.UnionWith(await _db.ServiceReports.AsNoTracking()
            .Where(r => keys.Contains(r.BlobKey)).Select(r => r.BlobKey).ToListAsync(ct));

        var orphans = keys.Where(k => !referenced.Contains(k)).ToList();
        foreach (var key in orphans)
        {
            await _storage.DeleteAsync(key, ct);
            _logger.LogInformation("Deleted orphaned file {Key}: no photo, signature or report points at it", key);
        }
        return orphans;
    }
}

public sealed record OrphanSweepResult(int Scanned, IReadOnlyList<string> Deleted);
