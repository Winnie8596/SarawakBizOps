using System.Collections.Concurrent;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// The <see cref="IFileStorage"/> fake: a dictionary with the same observable behaviour as
/// <c>AzureBlobFileStorage</c> (both pass <c>FileStorageContractTests</c>). Tests swap it in through
/// <c>WithWebHostBuilder</c> when they need to see exactly which files exist — e.g. that a rejected upload
/// left nothing behind — or to make storage fail on purpose (<see cref="FailSaves"/>).
/// </summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _files = new();

    /// <summary>When true, every save throws, as if the storage service were down.</summary>
    public bool FailSaves { get; set; }

    public IReadOnlyCollection<string> Keys => _files.Keys.ToList();

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        if (FailSaves)
        {
            throw new IOException("Simulated storage outage.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (!_files.TryAdd(key, (buffer.ToArray(), contentType)))
        {
            throw new InvalidOperationException($"A file is already stored under '{key}'.");
        }
    }

    public Task<StoredFile?> OpenReadAsync(string key, CancellationToken ct)
        => Task.FromResult(_files.TryGetValue(key, out var file)
            ? new StoredFile(new MemoryStream(file.Bytes, writable: false), file.ContentType, file.Bytes.Length)
            : null);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        _files.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
