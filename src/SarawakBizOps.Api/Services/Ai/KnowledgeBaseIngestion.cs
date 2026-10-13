using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The two non-evaluation callers of <see cref="IDocumentIngestionService"/> (AI-1, G8): startup when
/// <c>Ai:IngestOnStartup</c> is on (Development, the Docker demo), and the <c>ingest-kb</c> command for deployments:
/// <c>dotnet SarawakBizOps.Api.dll ingest-kb</c>.
/// </summary>
public static class KnowledgeBaseIngestion
{
    public const string Command = "ingest-kb";

    /// <summary>Startup: a problem is logged, never fatal — the rest of the app does not depend on the manuals.</summary>
    public static async Task IngestOnStartupAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<AiOptions>>().Value;
        if (!options.IngestOnStartup) return;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(KnowledgeBaseIngestion));
        try
        {
            await IngestAsync(services, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Knowledge base ingestion at startup failed; the assistant answers without manuals.");
        }
    }

    /// <summary>The command: exit code 0 when every file was ingested, 1 otherwise (so a deployment step fails).</summary>
    public static async Task<int> RunCommandAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var report = await IngestAsync(scope.ServiceProvider, ct);
        Console.WriteLine(
            $"Knowledge base: {report.Added} added, {report.Updated} updated, {report.Unchanged} unchanged, " +
            $"{report.Superseded} superseded, {report.Removed} removed.");
        foreach (var error in report.Errors)
        {
            Console.Error.WriteLine($"Not ingested: {error}");
        }
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private static Task<IngestionReport> IngestAsync(IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<AiOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();
        var folder = KnowledgeBaseLocator.Resolve(options.KnowledgeBasePath, environment.ContentRootPath);
        return services.GetRequiredService<IDocumentIngestionService>().IngestAsync(folder, ct);
    }
}
