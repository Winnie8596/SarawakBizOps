namespace SarawakBizOps.Api.Models.Entities;

/// <summary>
/// One retrievable piece of a manual (AI-1). <see cref="Embedding"/> is the vector as little-endian float32 bytes,
/// searched in memory rather than in SQL (G4); <see cref="EmbeddingModel"/> records which model produced it, so a
/// change of model re-embeds instead of mixing incomparable vectors.
/// </summary>
public class AiDocumentChunk
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public AiSourceDocument? Document { get; set; }

    /// <summary>Position in the document, from 0.</summary>
    public int Ordinal { get; set; }

    /// <summary>The heading path below the title, e.g. "Troubleshooting > E14 High head pressure".</summary>
    public string SectionHeading { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary><c>ceil(characters / 4)</c> (G18).</summary>
    public int TokenCount { get; set; }

    public byte[] Embedding { get; set; } = Array.Empty<byte>();

    public string EmbeddingModel { get; set; } = string.Empty;
}
