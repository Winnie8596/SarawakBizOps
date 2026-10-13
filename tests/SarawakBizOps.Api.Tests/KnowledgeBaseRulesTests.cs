using System.Text;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.Services.Ai;

namespace SarawakBizOps.Api.Tests;

/// <summary>AI-1, no database: identifier normalising, the Markdown chunker, front matter, and reading embedding
/// replies (docs/ai/decisions.md G3, G8, G15, G18).</summary>
public class KnowledgeBaseRulesTests
{
    private static readonly ChunkingSettings Small = new(ChunkTokens: 40, OverlapTokens: 10, MinTokens: 10);

    // ---- IdentifierNormaliser ----

    [Theory]
    [InlineData("kb-10t", "KB10T")]
    [InlineData("KB 10T", "KB10T")]
    [InlineData("KB-10T", "KB10T")]
    [InlineData(" kb‑10t ", "KB10T")] // non-breaking hyphen
    [InlineData("KB–10T", "KB10T")] // en dash
    [InlineData("Norvik", "NORVIK")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Identifiers_normalise_to_one_spelling(string? value, string expected)
    {
        Assert.Equal(expected, IdentifierNormaliser.Normalise(value));
    }

    // ---- MarkdownChunker ----

    [Fact]
    public void Tokens_are_characters_divided_by_four_rounded_up()
    {
        Assert.Equal(0, MarkdownChunker.CountTokens(""));
        Assert.Equal(1, MarkdownChunker.CountTokens("abcd"));
        Assert.Equal(2, MarkdownChunker.CountTokens("abcde"));
    }

    [Fact]
    public void Headings_are_boundaries_and_give_the_path_below_the_title()
    {
        var chunks = MarkdownChunker.Chunk("""
            # Norvik NC-450 Service Manual

            Intro text about the chiller.

            ## Troubleshooting

            ### E14 High head pressure

            Clean the condenser coil.

            ## Safety

            Isolate the supply.
            """, new ChunkingSettings(400, 50, 50));

        Assert.Equal(
            new[]
            {
                ("Norvik NC-450 Service Manual", "Intro text about the chiller."),
                ("Troubleshooting > E14 High head pressure", "Clean the condenser coil."),
                ("Safety", "Isolate the supply.")
            },
            chunks.Select(c => (c.SectionHeading, c.Text)));
        Assert.Equal(new[] { 0, 1, 2 }, chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void A_heading_inside_a_code_fence_is_text()
    {
        var chunks = MarkdownChunker.Chunk("## Wiring\n\n```\n# not a heading\nL1 L2 L3\n```\n", new ChunkingSettings(400, 50, 50));

        var chunk = Assert.Single(chunks);
        Assert.Equal("Wiring", chunk.SectionHeading);
        Assert.Contains("# not a heading", chunk.Text);
    }

    [Fact]
    public void Paragraphs_are_packed_up_to_the_limit_and_the_next_chunk_overlaps()
    {
        // Each paragraph is two sentences, 12 tokens: three fit in 40 tokens, four do not.
        var paragraphs = Enumerable.Range(1, 4)
            .Select(i => $"Step {i} checks the part. Step {i} then records it.")
            .ToList();
        var chunks = MarkdownChunker.Chunk("## Procedure\n\n" + string.Join("\n\n", paragraphs), Small);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= Small.ChunkTokens, $"{c.TokenCount}: {c.Text}"));
        Assert.All(chunks, c => Assert.Equal("Procedure", c.SectionHeading));
        // The second chunk starts with the last sentence of the first.
        var lastSentenceOfFirst = chunks[0].Text.Split(". ").Last();
        Assert.StartsWith(lastSentenceOfFirst.TrimEnd('.'), chunks[1].Text);
        // Nothing is lost.
        foreach (var paragraph in paragraphs)
        {
            Assert.Contains(chunks, c => c.Text.Contains(paragraph.Split(". ")[0]));
        }
    }

    [Fact]
    public void A_small_last_piece_is_merged_into_the_chunk_before()
    {
        var big = string.Join(" ", Enumerable.Repeat("Check the fan.", 10)); // 38 tokens
        var chunks = MarkdownChunker.Chunk($"## Fan\n\n{big}\n\nDone and dusted, all good.", Small); // + 7 tokens

        // 45 tokens do not fit in 40, but a 7-token chunk is below the minimum of 10, so it joins the one before.
        var chunk = Assert.Single(chunks);
        Assert.StartsWith(big, chunk.Text);
        Assert.EndsWith("Done and dusted, all good.", chunk.Text);
    }

    [Fact]
    public void A_short_section_on_its_own_is_kept()
    {
        var chunks = MarkdownChunker.Chunk("## Note\n\nOK.", Small);

        Assert.Equal("OK.", Assert.Single(chunks).Text);
    }

    [Fact]
    public void An_oversized_sentence_is_cut_by_words_and_never_exceeds_the_limit()
    {
        var sentence = string.Join(" ", Enumerable.Repeat("word", 200)); // 999 characters, no full stop
        var chunks = MarkdownChunker.Chunk($"## Long\n\n{sentence}", Small);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= Small.ChunkTokens));
        // Pieces cut by words have no sentence end to carry over, so every word appears exactly once.
        Assert.Equal(200, chunks.Sum(c => c.Text.Split(' ').Length));
    }

    [Fact]
    public void Windows_line_endings_give_the_same_chunks()
    {
        const string text = "## A\n\nOne.\n\n## B\n\nTwo.\n";
        Assert.Equal(
            MarkdownChunker.Chunk(text, Small),
            MarkdownChunker.Chunk(text.Replace("\n", "\r\n"), Small));
    }

    [Fact]
    public void The_config_id_names_all_three_settings()
    {
        Assert.Equal("md-400-50-50", new ChunkingSettings(400, 50, 50).ConfigId);
    }

    // ---- KnowledgeBaseFileParser ----

    private const string Valid = """
        ---
        slug: norvik-nc-chillers
        title: "Norvik NC-series chillers"
        brand: Norvik
        models: [NC-450, NC-300]
        version: 2.1
        ---
        # Norvik NC-series chillers

        Text.
        """;

    [Fact]
    public void Valid_front_matter_is_read()
    {
        var (file, error) = KnowledgeBaseFileParser.Parse(Valid);

        Assert.Null(error);
        Assert.Equal("norvik-nc-chillers", file!.Slug);
        Assert.Equal("Norvik NC-series chillers", file.Title);
        Assert.Equal("Norvik", file.Brand);
        Assert.Equal(new[] { "NC-450", "NC-300" }, file.Models);
        Assert.Equal("2.1", file.Version);
        Assert.Null(file.Supersedes);
        Assert.StartsWith("# Norvik NC-series chillers", file.Body);
    }

    [Fact]
    public void Models_may_be_one_per_line()
    {
        var (file, error) = KnowledgeBaseFileParser.Parse(
            "---\nslug: a\ntitle: A\nbrand: B\nmodels:\n  - X-1\n  - X-2\nversion: 1\nsupersedes: a-old\n---\nText.");

        Assert.Null(error);
        Assert.Equal(new[] { "X-1", "X-2" }, file!.Models);
        Assert.Equal("a-old", file.Supersedes);
    }

    [Theory]
    [InlineData("No front matter.", "does not start")]
    [InlineData("---\nslug: a\n", "not closed")]
    [InlineData("---\nslug: a\nmodel: X\n---\nText", "Unknown front matter key 'model'")]
    [InlineData("---\nslug: a\nslug: b\n---\nText", "appears twice")]
    [InlineData("---\nslug: Bad Slug\ntitle: A\nbrand: B\nmodels: [X]\nversion: 1\n---\nText", "slug must be")]
    [InlineData("---\nslug: a\nbrand: B\nmodels: [X]\nversion: 1\n---\nText", "title is required")]
    [InlineData("---\nslug: a\ntitle: A\nmodels: [X]\nversion: 1\n---\nText", "brand is required")]
    [InlineData("---\nslug: a\ntitle: A\nbrand: B\nmodels: []\nversion: 1\n---\nText", "at least one model")]
    [InlineData("---\nslug: a\ntitle: A\nbrand: B\nmodels: X\nversion: 1\n---\nText", "models must be")]
    [InlineData("---\nslug: a\ntitle: A\nbrand: B\nmodels: [X]\n---\nText", "version is required")]
    [InlineData("---\nslug: a\ntitle: A\nbrand: B\nmodels: [X]\nversion: 1\nsupersedes: a\n---\nText", "another document")]
    [InlineData("---\nslug: a\ntitle: A\nbrand: B\nmodels: [X]\nversion: 1\n---\n\n  \n", "no text")]
    public void Bad_front_matter_is_refused_with_a_reason(string content, string expected)
    {
        var (file, error) = KnowledgeBaseFileParser.Parse(content);

        Assert.Null(file);
        Assert.Contains(expected, error);
    }

    // ---- Embeddings ----

    [Fact]
    public void Embedding_bytes_round_trip_as_little_endian_float32()
    {
        var vector = new[] { 0.5f, -1.25f, 3f };
        var bytes = EmbeddingBytes.ToBytes(vector);

        Assert.Equal(12, bytes.Length);
        Assert.Equal(new byte[] { 0, 0, 0, 0x3F }, bytes[..4]); // 0.5f
        Assert.Equal(vector, EmbeddingBytes.ToVector(bytes));
    }

    [Fact]
    public void The_mock_is_deterministic_normalised_and_closer_for_shared_words()
    {
        var mock = new MockEmbeddingClient(Options.Create(new AiOptions { EmbeddingDimension = 64 }));

        var a = mock.Embed("NC-450 condenser fan noisy");
        Assert.Equal(a, mock.Embed("nc-450 CONDENSER fan noisy")); // case and dash forms of one identifier
        Assert.Equal(1.0, Math.Sqrt(a.Sum(v => v * v)), precision: 4);

        var related = Dot(a, mock.Embed("condenser fan bearing"));
        var unrelated = Dot(a, mock.Embed("forklift battery terminals"));
        Assert.True(related > unrelated, $"{related} vs {unrelated}");
    }

    [Fact]
    public void An_embeddings_reply_is_read_in_index_order_and_normalised()
    {
        var reply = Encoding.UTF8.GetBytes("""
            {"data":[{"index":1,"embedding":[0,2]},{"index":0,"embedding":[3,4]}]}
            """);

        var vectors = OpenAiCompatibleEmbeddingClient.ReadVectors(reply, expectedCount: 2, expectedDimension: 2);

        Assert.Equal(new[] { 0.6f, 0.8f }, vectors[0]);
        Assert.Equal(new[] { 0f, 1f }, vectors[1]);
    }

    [Theory]
    [InlineData("""{"data":[{"index":0,"embedding":[1,2,3]}]}""", "3 dimensions")]
    [InlineData("""{"data":[]}""", "wrong number")]
    [InlineData("""{"data":[{"index":5,"embedding":[1,2]}]}""", "bad index")]
    [InlineData("""{"error":"quota"}""", "not an embeddings response")]
    [InlineData("not json", "not an embeddings response")]
    public void A_bad_embeddings_reply_is_refused(string reply, string expected)
    {
        var ex = Assert.Throws<LlmProviderException>(() =>
            OpenAiCompatibleEmbeddingClient.ReadVectors(Encoding.UTF8.GetBytes(reply), 1, 2));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void The_embeddings_url_is_appended_to_the_base_url()
    {
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/embeddings",
            OpenAiCompatibleEmbeddingClient.EmbeddingsUri("https://generativelanguage.googleapis.com/v1beta/openai/").ToString());
    }

    [Fact]
    public void The_knowledge_base_is_found_in_a_folder_above_the_app()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var kb = Directory.CreateDirectory(Path.Combine(root.FullName, "knowledge-base"));
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "Api"));

            Assert.Equal(kb.FullName, KnowledgeBaseLocator.Resolve("knowledge-base", app.FullName));
            Assert.Equal("/abs/kb", KnowledgeBaseLocator.Resolve("/abs/kb", app.FullName).Replace('\\', '/'));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static double Dot(float[] a, float[] b) => a.Zip(b, (x, y) => (double)x * y).Sum();
}
