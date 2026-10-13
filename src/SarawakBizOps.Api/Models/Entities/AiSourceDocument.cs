using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Models.Entities;

/// <summary>
/// One manual from <c>knowledge-base/</c> (AI-1, docs/ai/decisions.md G8). The file is the source of truth: this row
/// records what was last ingested from it, so an unchanged file (same <see cref="ContentHash"/> and
/// <see cref="ChunkingConfigId"/>) is skipped without calling the embedding provider (G15).
/// </summary>
public class AiSourceDocument
{
    public int Id { get; set; }

    /// <summary>From the front matter; unique, and what <see cref="Supersedes"/> points at.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>The file's path inside <c>knowledge-base/</c>, so a file whose front matter breaks keeps its last
    /// good version rather than being marked removed.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Supersedes { get; set; }

    /// <summary>Hex SHA-256 of the file (line endings normalised to LF).</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary><see cref="Services.Ai.ChunkingSettings.ConfigId"/> the chunks were cut with, e.g. <c>md-400-50-50</c>.</summary>
    public string ChunkingConfigId { get; set; } = string.Empty;

    public AiDocumentStatus Status { get; set; } = AiDocumentStatus.Active;

    public DateTime IngestedAt { get; set; }

    public List<AiSourceDocumentModel> Models { get; set; } = new();
    public List<AiDocumentChunk> Chunks { get; set; } = new();
}
