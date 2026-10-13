using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Ai;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// AI-1 against the real database (docs/ai/decisions.md G8, G15): what ingestion stores, what it skips, and that a
/// failure never leaves a document half-replaced. Each test works in its own temporary folder with its own slugs, so
/// it cannot see or disturb another test's documents.
/// </summary>
[Collection(ApiCollection.Name)]
public class KnowledgeBaseIngestionTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("kb-");
    private readonly string _prefix = "t" + Guid.NewGuid().ToString("N")[..8];

    public KnowledgeBaseIngestionTests(ApiFactory factory) => _factory = factory;

    public void Dispose() => _folder.Delete(recursive: true);

    /// <summary>The mock, counting calls; it fails on demand to prove a failure keeps the previous version.</summary>
    private sealed class CountingEmbeddings : IEmbeddingClient
    {
        private readonly MockEmbeddingClient _inner = new(Options.Create(new AiOptions { EmbeddingDimension = 32 }));

        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public string ModelName { get; set; } = MockEmbeddingClient.Name;
        public int Dimension => _inner.Dimension;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new LlmProviderException("The embedding provider answered HTTP 503.");
            return _inner.EmbedAsync(texts, ct);
        }
    }

    private string Slug(string name) => $"{_prefix}-{name}";

    private void Write(string name, string body, string? supersedes = null, string models = "[NC-450, nc 300]",
        string version = "1")
    {
        File.WriteAllText(Path.Combine(_folder.FullName, $"{Slug(name)}.md"), $"""
            ---
            slug: {Slug(name)}
            title: Manual {name}
            brand: Norvik
            models: {models}
            version: {version}
            {(supersedes is null ? "" : $"supersedes: {Slug(supersedes)}")}
            ---
            # Manual {name}

            {body}
            """);
    }

    private async Task<IngestionReport> IngestAsync(CountingEmbeddings embeddings, AiOptions? options = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new DocumentIngestionService(db, embeddings, Options.Create(options ?? new AiOptions()),
            TimeProvider.System, NullLogger<DocumentIngestionService>.Instance);
        return await service.IngestAsync(_folder.FullName, CancellationToken.None);
    }

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<Models.Entities.AiSourceDocument> DocumentAsync(string name) => QueryAsync(db => db.AiSourceDocuments
        .Include(d => d.Models).Include(d => d.Chunks).AsNoTracking().SingleAsync(d => d.Slug == Slug(name)));

    [Fact]
    public async Task A_new_file_is_stored_with_normalised_models_and_embedded_chunks()
    {
        Write("new", "## Troubleshooting\n\n### E14 High head pressure\n\nClean the condenser coil.");
        var embeddings = new CountingEmbeddings();

        var report = await IngestAsync(embeddings);

        Assert.Equal(1, report.Added);
        Assert.Empty(report.Errors);
        var document = await DocumentAsync("new");
        Assert.Equal((AiDocumentStatus.Active, "Manual new", "1", "md-400-50-50"),
            (document.Status, document.Title, document.Version, document.ChunkingConfigId));
        Assert.Equal(64, document.ContentHash.Length);
        Assert.Equal(new[] { ("NORVIK", "NC300"), ("NORVIK", "NC450") },
            document.Models.Select(m => (m.NormalisedBrand, m.NormalisedModel)).Order());

        var chunk = Assert.Single(document.Chunks);
        Assert.Equal("Troubleshooting > E14 High head pressure", chunk.SectionHeading);
        Assert.Equal("Clean the condenser coil.", chunk.Text);
        Assert.Equal(MockEmbeddingClient.Name, chunk.EmbeddingModel);
        Assert.Equal(32, EmbeddingBytes.ToVector(chunk.Embedding).Length);
    }

    [Fact]
    public async Task An_unchanged_file_is_skipped_without_calling_the_embedding_provider()
    {
        Write("same", "## A\n\nText.");
        var embeddings = new CountingEmbeddings();
        await IngestAsync(embeddings);
        var calls = embeddings.Calls;
        var before = await DocumentAsync("same");

        var report = await IngestAsync(embeddings);

        Assert.Equal((0, 0, 1), (report.Added, report.Updated, report.Unchanged));
        Assert.Equal(calls, embeddings.Calls);
        Assert.Equal(before.Chunks.Single().Id, (await DocumentAsync("same")).Chunks.Single().Id);
    }

    [Fact]
    public async Task A_changed_chunking_configuration_re_chunks_an_unchanged_file()
    {
        Write("rechunk", "## A\n\nText.");
        var embeddings = new CountingEmbeddings();
        await IngestAsync(embeddings);

        var report = await IngestAsync(embeddings, new AiOptions { ChunkTokens = 250, ChunkOverlapTokens = 40 });

        Assert.Equal(1, report.Updated);
        Assert.Equal("md-250-40-50", (await DocumentAsync("rechunk")).ChunkingConfigId);
    }

    [Fact]
    public async Task A_different_embedding_model_re_embeds_an_unchanged_file()
    {
        Write("model", "## A\n\nText.");
        await IngestAsync(new CountingEmbeddings());

        var report = await IngestAsync(new CountingEmbeddings { ModelName = "other-model" });

        Assert.Equal(1, report.Updated);
        Assert.Equal("other-model", (await DocumentAsync("model")).Chunks.Single().EmbeddingModel);
    }

    [Fact]
    public async Task A_changed_file_replaces_its_chunks_and_models()
    {
        Write("change", "## Old\n\nOld text.");
        var embeddings = new CountingEmbeddings();
        await IngestAsync(embeddings);

        Write("change", "## New\n\nNew text.\n\n## Second\n\nMore.", models: "[NC-600]", version: "2");
        var report = await IngestAsync(embeddings);

        Assert.Equal(1, report.Updated);
        var document = await DocumentAsync("change");
        Assert.Equal("2", document.Version);
        Assert.Equal(new[] { "NC600" }, document.Models.Select(m => m.NormalisedModel));
        Assert.Equal(new[] { ("New", 0), ("Second", 1) },
            document.Chunks.OrderBy(c => c.Ordinal).Select(c => (c.SectionHeading, c.Ordinal)));
    }

    [Fact]
    public async Task A_second_version_supersedes_the_first_and_removing_it_restores_the_first()
    {
        Write("v1", "## A\n\nOld procedure.");
        Write("v2", "## A\n\nNew procedure.", supersedes: "v1", version: "2");
        var embeddings = new CountingEmbeddings();

        var report = await IngestAsync(embeddings);

        Assert.Equal(1, report.Superseded);
        Assert.Equal(AiDocumentStatus.Superseded, (await DocumentAsync("v1")).Status);
        Assert.Equal(AiDocumentStatus.Active, (await DocumentAsync("v2")).Status);

        File.Delete(Path.Combine(_folder.FullName, $"{Slug("v2")}.md"));
        await IngestAsync(embeddings);

        Assert.Equal(AiDocumentStatus.Removed, (await DocumentAsync("v2")).Status);
        Assert.Equal(AiDocumentStatus.Active, (await DocumentAsync("v1")).Status);
    }

    [Fact]
    public async Task A_deleted_file_becomes_removed_and_comes_back_when_restored()
    {
        Write("gone", "## A\n\nText.");
        var embeddings = new CountingEmbeddings();
        await IngestAsync(embeddings);
        var path = Path.Combine(_folder.FullName, $"{Slug("gone")}.md");
        var content = await File.ReadAllTextAsync(path);

        File.Delete(path);
        var report = await IngestAsync(embeddings);
        Assert.Equal(1, report.Removed);
        Assert.Equal(AiDocumentStatus.Removed, (await DocumentAsync("gone")).Status);

        await File.WriteAllTextAsync(path, content);
        report = await IngestAsync(embeddings);
        Assert.Equal(1, report.Unchanged);
        Assert.Equal(AiDocumentStatus.Active, (await DocumentAsync("gone")).Status);
    }

    [Fact]
    public async Task An_embedding_failure_keeps_the_previous_version_and_is_reported()
    {
        Write("fail", "## A\n\nGood text.");
        var embeddings = new CountingEmbeddings();
        await IngestAsync(embeddings);

        Write("fail", "## A\n\nChanged text.", version: "2");
        embeddings.Fail = true;
        var report = await IngestAsync(embeddings);

        var error = Assert.Single(report.Errors);
        Assert.Contains($"{Slug("fail")}.md", error);
        Assert.Contains("HTTP 503", error);
        var document = await DocumentAsync("fail");
        Assert.Equal(("1", AiDocumentStatus.Active, "Good text."),
            (document.Version, document.Status, document.Chunks.Single().Text));
    }

    [Fact]
    public async Task Bad_front_matter_is_reported_and_its_last_good_version_is_kept()
    {
        Write("broken", "## A\n\nGood text.");
        var embeddings = new CountingEmbeddings();
        await IngestAsync(embeddings);

        var path = Path.Combine(_folder.FullName, $"{Slug("broken")}.md");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("models:", "model:"));
        var report = await IngestAsync(embeddings);

        Assert.Contains(report.Errors, e => e.Contains("Unknown front matter key 'model'"));
        var document = await DocumentAsync("broken");
        Assert.Equal((AiDocumentStatus.Active, "Good text."), (document.Status, document.Chunks.Single().Text));
    }

    [Fact]
    public async Task A_new_file_with_bad_front_matter_stores_nothing()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder.FullName, $"{Slug("bad")}.md"),
            $"---\nslug: {Slug("bad")}\ntitle: T\n---\nText.");

        var report = await IngestAsync(new CountingEmbeddings());

        Assert.Single(report.Errors);
        Assert.False(await QueryAsync(db => db.AiSourceDocuments.AnyAsync(d => d.Slug == Slug("bad"))));
    }

    [Fact]
    public async Task Two_files_with_one_slug_store_the_first_and_report_the_second()
    {
        Write("dup", "## A\n\nFirst.");
        File.Copy(Path.Combine(_folder.FullName, $"{Slug("dup")}.md"), Path.Combine(_folder.FullName, "zz-copy.md")); // sorts after it

        var report = await IngestAsync(new CountingEmbeddings());

        Assert.Contains(report.Errors, e => e.Contains("already used"));
        Assert.Equal($"{Slug("dup")}.md", (await DocumentAsync("dup")).SourcePath);
    }

    [Fact]
    public async Task The_readme_is_not_a_document()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder.FullName, "README.md"), "# Knowledge base\n\nAll synthetic.");

        var report = await IngestAsync(new CountingEmbeddings());

        Assert.Empty(report.Errors);
        Assert.Equal(0, report.Added);
    }

    [Fact]
    public async Task The_repository_knowledge_base_ingests_without_errors()
    {
        var folder = KnowledgeBaseLocator.Resolve("knowledge-base", AppContext.BaseDirectory);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new DocumentIngestionService(db, new CountingEmbeddings(), Options.Create(new AiOptions()),
            TimeProvider.System, NullLogger<DocumentIngestionService>.Instance);

        var report = await service.IngestAsync(folder, CancellationToken.None);

        Assert.Empty(report.Errors);
        Assert.True(report.Added + report.Updated + report.Unchanged >= 5);
    }
}
