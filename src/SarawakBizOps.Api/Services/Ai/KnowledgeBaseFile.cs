using System.Text.RegularExpressions;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>One <c>knowledge-base/*.md</c> file, its front matter checked (G8).</summary>
public record KnowledgeBaseFile(
    string Slug, string Title, string Brand, IReadOnlyList<string> Models, string Version, string? Supersedes,
    string Body);

/// <summary>
/// Reads a manual's front matter: a block between two <c>---</c> lines at the top of the file, one <c>key: value</c>
/// per line. <c>models</c> is either <c>[NC-450, NC-300]</c> or one <c>- NC-450</c> line per model. Strict on purpose
/// — an unknown key (a typo such as <c>model:</c>), a missing field or a bad slug is an error, reported by ingestion
/// rather than half-stored. Hand-written instead of a YAML package because the format is this small (PRD D-06).
/// </summary>
public static partial class KnowledgeBaseFileParser
{
    public const int MaxSlugLength = 100;
    public const int MaxTitleLength = 200;
    public const int MaxBrandLength = 100;
    public const int MaxModelLength = 100;
    public const int MaxVersionLength = 20;

    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
        { "slug", "title", "brand", "models", "version", "supersedes" };

    public static (KnowledgeBaseFile? File, string? Error) Parse(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (null, "The file does not start with a front matter block (---).");
        }

        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0)
        {
            return (null, "The front matter block is not closed (---).");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var models = new List<string>();
        string? listKey = null;

        for (var i = 1; i < end; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith("- ", StringComparison.Ordinal) && listKey == "models")
            {
                models.Add(Unquote(line[2..]));
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                return (null, $"Front matter line {i + 1} is not 'key: value'.");
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!Keys.Contains(key))
            {
                return (null, $"Unknown front matter key '{key}'.");
            }
            if (values.ContainsKey(key))
            {
                return (null, $"Front matter key '{key}' appears twice.");
            }

            values[key] = value;
            listKey = key == "models" && value.Length == 0 ? key : null;
            if (key == "models" && value.Length > 0)
            {
                if (!value.StartsWith('[') || !value.EndsWith(']'))
                {
                    return (null, "models must be [A, B] or one '- A' line per model.");
                }
                models.AddRange(value[1..^1].Split(',').Select(Unquote).Where(m => m.Length > 0));
            }
        }

        string Value(string key) => values.TryGetValue(key, out var v) ? Unquote(v) : string.Empty;

        var slug = Value("slug");
        var title = Value("title");
        var brand = Value("brand");
        var version = Value("version");
        var supersedes = Value("supersedes");

        if (!SlugPattern().IsMatch(slug) || slug.Length > MaxSlugLength)
        {
            return (null, "slug must be lower-case words joined by dashes, e.g. norvik-nc-chillers.");
        }
        if (title.Length == 0 || title.Length > MaxTitleLength)
        {
            return (null, $"title is required (at most {MaxTitleLength} characters).");
        }
        if (brand.Length == 0 || brand.Length > MaxBrandLength)
        {
            return (null, $"brand is required (at most {MaxBrandLength} characters).");
        }
        if (models.Count == 0 || models.Any(m => m.Length > MaxModelLength))
        {
            return (null, "models needs at least one model.");
        }
        if (version.Length == 0 || version.Length > MaxVersionLength)
        {
            return (null, $"version is required (at most {MaxVersionLength} characters).");
        }
        if (supersedes.Length > 0 && (!SlugPattern().IsMatch(supersedes) || supersedes == slug))
        {
            return (null, "supersedes must be another document's slug.");
        }

        var body = string.Join('\n', lines.Skip(end + 1)).Trim('\n');
        if (body.Trim().Length == 0)
        {
            return (null, "The document has no text after the front matter.");
        }

        return (new KnowledgeBaseFile(slug, title, brand, models.Distinct().ToList(), version,
            supersedes.Length > 0 ? supersedes : null, body), null);
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1].Trim()
            : value;
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
