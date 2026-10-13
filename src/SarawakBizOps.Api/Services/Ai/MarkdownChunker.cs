using System.Text;
using System.Text.RegularExpressions;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>How a manual is cut into chunks (AIplan §6). <see cref="ConfigId"/> is stored on each document, so
/// changing any of the three re-chunks every file on the next ingestion (G15).</summary>
public record ChunkingSettings(int ChunkTokens, int OverlapTokens, int MinTokens)
{
    public string ConfigId => $"md-{ChunkTokens}-{OverlapTokens}-{MinTokens}";
}

/// <param name="SectionHeading">The heading path below the title, e.g. "Troubleshooting > E14 High head pressure".</param>
public record MarkdownChunk(int Ordinal, string SectionHeading, string Text, int TokenCount);

/// <summary>
/// Cuts a Markdown manual into retrieval chunks (AI-1). Headings are hard boundaries: a chunk never spans two
/// sections, so every chunk can be cited by one heading path. Within a section, paragraphs are packed up to
/// <see cref="ChunkingSettings.ChunkTokens"/>; a paragraph too long on its own is split into sentences, and a sentence
/// too long on its own into words. Each new chunk in a section starts with the last sentences of the one before (up
/// to <see cref="ChunkingSettings.OverlapTokens"/>), so a step cut at a boundary keeps its context. A section's last
/// piece smaller than <see cref="ChunkingSettings.MinTokens"/> is merged into the chunk before it instead of standing
/// alone. Pure: no I/O, so every rule is unit-tested.
/// <para>
/// Tokens are counted as <c>ceil(characters / 4)</c> (G18): provider-independent and close enough for a 400-token
/// target. The level-1 heading is the document's title and is left out of the path (the title is stored separately).
/// </para>
/// </summary>
public static partial class MarkdownChunker
{
    public static int CountTokens(string text) => (text.Length + 3) / 4;

    public static IReadOnlyList<MarkdownChunk> Chunk(string markdown, ChunkingSettings settings)
    {
        var chunks = new List<MarkdownChunk>();
        foreach (var section in Sections(markdown))
        {
            foreach (var text in PackSection(section.Blocks, settings))
            {
                chunks.Add(new MarkdownChunk(chunks.Count, section.Heading, text, CountTokens(text)));
            }
        }
        return chunks;
    }

    private record Section(string Heading, List<string> Blocks);

    // A piece of a section's text, remembering which block (paragraph, list, table) it came from, so pieces of one
    // block are joined with a space and different blocks with a blank line.
    private record Piece(string Text, int Block);

    private static List<Section> Sections(string markdown)
    {
        var sections = new List<Section>();
        var headings = new string?[7];
        var current = new Section(string.Empty, new List<string>());
        var block = new StringBuilder();
        var inFence = false;

        void EndBlock()
        {
            var text = block.ToString().Trim();
            if (text.Length > 0) current.Blocks.Add(text);
            block.Clear();
        }

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                block.Append(line).Append('\n');
                continue;
            }

            var heading = inFence ? Match.Empty : HeadingPattern().Match(line);
            if (heading.Success)
            {
                EndBlock();
                if (current.Blocks.Count > 0) sections.Add(current);

                var level = heading.Groups[1].Length;
                headings[level] = heading.Groups[2].Value.Trim();
                for (var deeper = level + 1; deeper < headings.Length; deeper++) headings[deeper] = null;

                current = new Section(HeadingPath(headings), new List<string>());
                continue;
            }

            if (!inFence && line.Length == 0)
            {
                EndBlock();
                continue;
            }

            block.Append(line).Append('\n');
        }

        EndBlock();
        if (current.Blocks.Count > 0) sections.Add(current);
        return sections;
    }

    private static string HeadingPath(string?[] headings)
    {
        var below = headings.Skip(2).Where(h => h is not null).ToList();
        return below.Count > 0 ? string.Join(" > ", below) : headings[1] ?? string.Empty;
    }

    private static List<string> PackSection(List<string> blocks, ChunkingSettings settings)
    {
        var pieces = new List<Piece>();
        for (var i = 0; i < blocks.Count; i++)
        {
            pieces.AddRange(Split(blocks[i], settings.ChunkTokens).Select(text => new Piece(text, i)));
        }

        var chunks = new List<List<Piece>>();
        var overlap = new List<Piece>(); // carried from the previous chunk; not "new" text
        var fresh = new List<Piece>();

        foreach (var piece in pieces)
        {
            if (fresh.Count > 0 && CountTokens(Join(overlap.Concat(fresh).Append(piece))) > settings.ChunkTokens)
            {
                chunks.Add(overlap.Concat(fresh).ToList());
                var tail = Tail(fresh[^1], settings.OverlapTokens);
                overlap = tail is not null && CountTokens(Join(new[] { tail, piece })) <= settings.ChunkTokens
                    ? new List<Piece> { tail }
                    : new List<Piece>();
                fresh = new List<Piece>();
            }
            fresh.Add(piece);
        }

        if (fresh.Count > 0)
        {
            // A small last piece joins the chunk before it (without the overlap, which that chunk already holds).
            if (chunks.Count > 0 && CountTokens(Join(fresh)) < settings.MinTokens)
            {
                chunks[^1].AddRange(fresh);
            }
            else
            {
                chunks.Add(overlap.Concat(fresh).ToList());
            }
        }

        return chunks.Select(Join).ToList();
    }

    // A block that fits is one piece; otherwise its sentences; a sentence that still does not fit is cut by words.
    private static IEnumerable<string> Split(string block, int maxTokens)
    {
        if (CountTokens(block) <= maxTokens) return new[] { block };

        return Sentences(block).SelectMany(sentence => CountTokens(sentence) <= maxTokens
            ? new[] { sentence }
            : Words(sentence, maxTokens * 4));
    }

    private static IEnumerable<string> Sentences(string text)
        => SentenceEnd().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);

    private static IEnumerable<string> Words(string text, int maxChars)
    {
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            // A single "word" longer than a whole chunk (a URL, a hash) is cut where it must be.
            for (var rest = word; rest.Length > 0;)
            {
                var take = Math.Min(rest.Length, maxChars - current.Length);
                current.Append(rest, 0, take);
                rest = rest[take..];
                if (rest.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    // The last whole sentences of a piece that fit the overlap budget; null when not even one does.
    private static Piece? Tail(Piece piece, int overlapTokens)
    {
        if (overlapTokens <= 0) return null;

        var taken = new List<string>();
        foreach (var sentence in Sentences(piece.Text).Reverse())
        {
            if (CountTokens(string.Join(' ', taken.Prepend(sentence))) > overlapTokens) break;
            taken.Insert(0, sentence);
        }
        return taken.Count > 0 ? new Piece(string.Join(' ', taken), piece.Block) : null;
    }

    private static string Join(IEnumerable<Piece> pieces)
    {
        var text = new StringBuilder();
        Piece? previous = null;
        foreach (var piece in pieces)
        {
            if (previous is not null) text.Append(previous.Block == piece.Block ? " " : "\n\n");
            text.Append(piece.Text);
            previous = piece;
        }
        return text.ToString();
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*$")]
    private static partial Regex HeadingPattern();

    // After ., ! or ? followed by whitespace — "E14. Check" splits, "1.5 bar" and "NC-450" do not.
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();
}
