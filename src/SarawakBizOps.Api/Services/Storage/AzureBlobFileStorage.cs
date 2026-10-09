using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace SarawakBizOps.Api.Services.Storage;

/// <summary>
/// <see cref="IFileStorage"/> on Azure Blob Storage — Azurite in development, Docker and the integration
/// tests (Open Question 2, decided in Phase 8). The container is private (no public access level), so a
/// blob can only be read through the API. It is created on first use, which keeps startup independent of
/// whether the emulator is up yet.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private volatile bool _containerReady;

    public AzureBlobFileStorage(BlobContainerClient container) => _container = container;

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            // Keys are unique per upload: refuse to overwrite rather than silently replace another file.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, ct);
    }

    public async Task<StoredFile?> OpenReadAsync(string key, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        try
        {
            var download = await _container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct);
            var details = download.Value.Details;
            return new StoredFile(download.Value.Content, details.ContentType ?? "application/octet-stream", details.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async IAsyncEnumerable<StoredFileInfo> ListAsync(
        string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            // Every blob has a Last-Modified time; if one ever came back without it, MaxValue keeps it from ever
            // looking old enough for the orphan sweep to touch.
            yield return new StoredFileInfo(blob.Name, blob.Properties.LastModified ?? DateTimeOffset.MaxValue);
        }
    }

    // Idempotent, so two first calls racing here is harmless; a failure (emulator not up yet) is simply
    // retried by the next call instead of being remembered.
    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_containerReady)
        {
            return;
        }

        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _containerReady = true;
    }
}
