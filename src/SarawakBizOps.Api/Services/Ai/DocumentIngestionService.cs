using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.Ai;

/// <param name="Errors">One line per file that could not be ingested, naming the file; that file's previous
/// version (if any) is left exactly as it was.</param>
public record IngestionReport(int Added, int Updated, int Unchanged, int Superseded, int Removed, IReadOnlyList<string> Errors);

public interface IDocumentIngestionService
{
    /// <summary>Brings the database in line with the <c>*.md</c> files in <paramref name="folder"/> (README.md
    /// excepted). Safe to run repeatedly: unchanged files cost nothing.</summary>
    Task<IngestionReport> IngestAsync(string folder, CancellationToken ct);
}

/// <summary>
/// Ingestion for the documents-as-code knowledge base (AI-1, docs/ai/decisions.md G8, G15). One code path for its
/// three callers: Development startup, the <c>ingest-kb</c> command and (AI-2) the evaluation runner.
/// <list type="number">
/// <item>Each file's front matter is checked; a bad file is reported and skipped, never half-stored.</item>
/// <item>A file is skipped when its content hash, the chunking configuration and the embedding model all match what
/// is stored, so a repeat run makes no embedding calls.</item>
/// <item>Otherwise it is chunked and embedded first, then its old chunks are replaced in one transaction: an
/// embedding or database failure leaves the previous version in place.</item>
/// <item>Last, statuses: a document named by another's <c>supersedes</c> is Superseded, one whose file is gone is
/// Removed, the rest Active. Only Active documents are ever retrieved.</item>
/// </list>
/// Writes through the app's own connection: the AI reader login is SELECT-only by design.
/// </summary>
public class DocumentIngestionService : IDocumentIngestionService
{
    public const string ReadmeFileName = "README.md";

    private readonly ApplicationDbContext _db;
    private readonly IEmbeddingClient _embeddings;
    private readonly AiOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(ApplicationDbContext db, IEmbeddingClient embeddings, IOptions<AiOptions> options,
        TimeProvider clock, ILogger<DocumentIngestionService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IngestionReport> IngestAsync(string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"The knowledge base folder '{folder}' does not exist.");
        }

        var chunking = _options.GetChunking();
        var documents = await _db.AiSourceDocuments.Include(d => d.Models).ToDictionaryAsync(d => d.Slug, ct);
        var staleModelIds = (await _db.AiDocumentChunks
                .Where(c => c.EmbeddingModel != _embeddings.ModelName)
                .Select(c => c.DocumentId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var present = new HashSet<string>(StringComparer.Ordinal);    // slugs whose file parsed
        var brokenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        int added = 0, updated = 0, unchanged = 0;

        var files = Directory.GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), ReadmeFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            var content = (await File.ReadAllTextAsync(path, ct)).Replace("\r\n", "\n");

            var (file, error) = KnowledgeBaseFileParser.Parse(content);
            if (file is null)
            {
                errors.Add($"{fileName}: {error}");
                brokenPaths.Add(fileName);
                continue;
            }
            if (!present.Add(file.Slug))
            {
                errors.Add($"{fileName}: slug '{file.Slug}' is already used by another file.");
                brokenPaths.Add(fileName);
                continue;
            }

            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            documents.TryGetValue(file.Slug, out var document);

            if (document is not null && document.ContentHash == hash && document.ChunkingConfigId == chunking.ConfigId
                && !staleModelIds.Contains(document.Id))
            {
                document.SourcePath = fileName; // a renamed file keeps its document
                unchanged++;
                continue;
            }

            var chunks = MarkdownChunker.Chunk(file.Body, chunking);
            IReadOnlyList<float[]> vectors;
            try
            {
                vectors = await EmbedAsync(file, chunks, ct);
            }
            catch (LlmProviderException ex)
            {
                errors.Add($"{fileName}: embedding failed ({ex.Message}); the previous version is kept.");
                brokenPaths.Add(fileName);
                continue;
            }

            var isNew = document is null;
            document = await StoreAsync(document, file, fileName, hash, chunking.ConfigId, chunks, vectors, ct);
            documents[file.Slug] = document;
            if (isNew) added++; else updated++;
        }

        var (superseded, removed) = await UpdateStatusesAsync(documents.Values, present, brokenPaths, ct);

        _logger.LogInformation(
            "Knowledge base ingested from {Folder}: {Added} added, {Updated} updated, {Unchanged} unchanged, " +
            "{Superseded} superseded, {Removed} removed, {Errors} errors.",
            folder, added, updated, unchanged, superseded, removed, errors.Count);
        foreach (var error in errors)
        {
            _logger.LogWarning("Knowledge base file not ingested: {Error}", error);
        }

        return new IngestionReport(added, updated, unchanged, superseded, removed, errors);
    }

    /// <summary>What is embedded for a chunk: its document and heading path as well as its text, so a chunk that
    /// only says "check the fan" is still found for "NC-450 condenser fan".</summary>
    public static string EmbeddingInput(string title, MarkdownChunk chunk)
        => $"{title}\n{chunk.SectionHeading}\n\n{chunk.Text}";

    private async Task<IReadOnlyList<float[]>> EmbedAsync(KnowledgeBaseFile file, IReadOnlyList<MarkdownChunk> chunks,
        CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.EmbeddingBatchSize);
        var vectors = new List<float[]>(chunks.Count);
        foreach (var batch in chunks.Chunk(batchSize))
        {
            var result = await _embeddings.EmbedAsync(batch.Select(c => EmbeddingInput(file.Title, c)).ToList(), ct);
            if (result.Count != batch.Length)
            {
                throw new LlmProviderException("The embedding provider returned the wrong number of vectors.");
            }
            vectors.AddRange(result);
        }
        return vectors;
    }

    private async Task<AiSourceDocument> StoreAsync(AiSourceDocument? document, KnowledgeBaseFile file, string fileName,
        string hash, string chunkingConfigId, IReadOnlyList<MarkdownChunk> chunks, IReadOnlyList<float[]> vectors,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        if (document is null)
        {
            document = new AiSourceDocument { Slug = file.Slug, Status = AiDocumentStatus.Active };
            _db.AiSourceDocuments.Add(document);
        }
        else
        {
            await _db.AiDocumentChunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(ct);
            document.Models.Clear();
        }

        document.SourcePath = fileName;
        document.Title = file.Title;
        document.Brand = file.Brand;
        document.Version = file.Version;
        document.Supersedes = file.Supersedes;
        document.ContentHash = hash;
        document.ChunkingConfigId = chunkingConfigId;
        document.IngestedAt = _clock.GetUtcNow().UtcDateTime;

        var brand = IdentifierNormaliser.Normalise(file.Brand);
        foreach (var model in file.Models.Select(IdentifierNormaliser.Normalise).Distinct())
        {
            document.Models.Add(new AiSourceDocumentModel { NormalisedBrand = brand, NormalisedModel = model });
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            _db.AiDocumentChunks.Add(new AiDocumentChunk
            {
                Document = document,
                Ordinal = chunks[i].Ordinal,
                SectionHeading = chunks[i].SectionHeading,
                Text = chunks[i].Text,
                TokenCount = chunks[i].TokenCount,
                Embedding = EmbeddingBytes.ToBytes(vectors[i]),
                EmbeddingModel = _embeddings.ModelName
            });
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // The chunks are not needed in memory again; do not keep thousands of floats tracked.
        foreach (var entry in _db.ChangeTracker.Entries<AiDocumentChunk>().ToList())
        {
            entry.State = EntityState.Detached;
        }
        return document;
    }

    private async Task<(int Superseded, int Removed)> UpdateStatusesAsync(IEnumerable<AiSourceDocument> documents,
        IReadOnlySet<string> present, IReadOnlySet<string> brokenPaths, CancellationToken ct)
    {
        var all = documents.ToList();
        var supersededSlugs = all
            .Where(d => present.Contains(d.Slug) && d.Supersedes is not null)
            .Select(d => d.Supersedes!)
            .ToHashSet(StringComparer.Ordinal);

        int superseded = 0, removed = 0;
        foreach (var document in all)
        {
            AiDocumentStatus status;
            if (!present.Contains(document.Slug))
            {
                // A file that is there but unreadable keeps its last good version and status.
                if (brokenPaths.Contains(document.SourcePath)) continue;
                status = AiDocumentStatus.Removed;
            }
            else
            {
                status = supersededSlugs.Contains(document.Slug) ? AiDocumentStatus.Superseded : AiDocumentStatus.Active;
            }

            if (status == document.Status) continue;
            document.Status = status;
            if (status == AiDocumentStatus.Superseded) superseded++;
            if (status == AiDocumentStatus.Removed) removed++;
        }

        await _db.SaveChangesAsync(ct);
        return (superseded, removed);
    }
}

/// <summary>Stands in when a real provider is configured without <c>Ai:EmbeddingModel</c>: chat keeps working, and
/// anything that needs a vector fails with a clear message instead of the app refusing to start.</summary>
public class UnconfiguredEmbeddingClient : IEmbeddingClient
{
    public string ModelName => "unconfigured";

    public int Dimension => 0;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        => throw new LlmProviderException("Ai:EmbeddingModel is not set.");
}

/// <summary>Finds the knowledge base folder (see <see cref="AiOptions.KnowledgeBasePath"/>).</summary>
public static class KnowledgeBaseLocator
{
    public static string Resolve(string configuredPath, string contentRoot)
    {
        if (Path.IsPathRooted(configuredPath)) return configuredPath;

        for (var dir = new DirectoryInfo(contentRoot); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, configuredPath);
            if (Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(contentRoot, configuredPath);
    }
}
