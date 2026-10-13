using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Services.Ai;

namespace SarawakBizOps.Api.Data.Configurations;

public class AiSourceDocumentConfiguration : IEntityTypeConfiguration<AiSourceDocument>
{
    public void Configure(EntityTypeBuilder<AiSourceDocument> builder)
    {
        builder.Property(d => d.Slug).IsRequired().HasMaxLength(KnowledgeBaseFileParser.MaxSlugLength);
        builder.HasIndex(d => d.Slug).IsUnique();
        builder.Property(d => d.SourcePath).IsRequired().HasMaxLength(260);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(KnowledgeBaseFileParser.MaxTitleLength);
        builder.Property(d => d.Brand).IsRequired().HasMaxLength(KnowledgeBaseFileParser.MaxBrandLength);
        builder.Property(d => d.Version).IsRequired().HasMaxLength(KnowledgeBaseFileParser.MaxVersionLength);
        builder.Property(d => d.Supersedes).HasMaxLength(KnowledgeBaseFileParser.MaxSlugLength);
        builder.Property(d => d.ContentHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(d => d.ChunkingConfigId).IsRequired().HasMaxLength(50);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
    }
}

public class AiSourceDocumentModelConfiguration : IEntityTypeConfiguration<AiSourceDocumentModel>
{
    public void Configure(EntityTypeBuilder<AiSourceDocumentModel> builder)
    {
        builder.Property(m => m.NormalisedBrand).IsRequired().HasMaxLength(KnowledgeBaseFileParser.MaxBrandLength);
        builder.Property(m => m.NormalisedModel).IsRequired().HasMaxLength(KnowledgeBaseFileParser.MaxModelLength);
        // The equipment filter (G3): which documents cover this brand + model.
        builder.HasIndex(m => new { m.NormalisedBrand, m.NormalisedModel });

        builder.HasOne(m => m.Document)
            .WithMany(d => d.Models)
            .HasForeignKey(m => m.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AiDocumentChunkConfiguration : IEntityTypeConfiguration<AiDocumentChunk>
{
    public void Configure(EntityTypeBuilder<AiDocumentChunk> builder)
    {
        builder.Property(c => c.SectionHeading).IsRequired().HasMaxLength(500);
        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.Embedding).IsRequired();
        builder.Property(c => c.EmbeddingModel).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => new { c.DocumentId, c.Ordinal }).IsUnique();

        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
