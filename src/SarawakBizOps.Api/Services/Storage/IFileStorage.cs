namespace SarawakBizOps.Api.Services.Storage;

/// <summary>
/// Blob storage for job files (SDD §15, PRD D-03): the database keeps only metadata and the internal
/// <c>key</c>, never the bytes and never a public URL, so every read goes back through an authorized API
/// endpoint. Keys are chosen by the caller and are unique per upload (a new key for every file, never an
/// overwrite), which lets a failed database write clean up after itself without touching the old file.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>Null when nothing is stored under <paramref name="key"/>. The caller disposes the stream.</summary>
    Task<StoredFile?> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>Idempotent: deleting a key that does not exist is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>Every stored file whose key starts with <paramref name="prefix"/>, and when it was written.</summary>
    IAsyncEnumerable<StoredFileInfo> ListAsync(string prefix, CancellationToken ct);
}

public sealed record StoredFile(Stream Content, string ContentType, long Length);

public sealed record StoredFileInfo(string Key, DateTimeOffset LastModified);
