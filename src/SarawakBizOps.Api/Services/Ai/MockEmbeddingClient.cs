using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// Deterministic, offline embeddings for development, tests and the Docker demo (AI-1): each word and identifier,
/// normalised, is hashed to one dimension, so texts that share words point the same way. It knows no meaning ("noisy"
/// and "bising" are unrelated to it), which is why the evaluation refuses to run on it.
/// </summary>
public partial class MockEmbeddingClient : IEmbeddingClient
{
    public const string Name = "mock-hash-v1";

    public MockEmbeddingClient(IOptions<AiOptions> options)
    {
        Dimension = Math.Max(8, options.Value.EmbeddingDimension);
    }

    public string ModelName => Name;

    public int Dimension { get; }

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    public float[] Embed(string text)
    {
        var vector = new float[Dimension];
        foreach (Match word in WordPattern().Matches(text))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(IdentifierNormaliser.Normalise(word.Value)));
            var index = (int)(BitConverter.ToUInt32(hash, 0) % (uint)Dimension);
            vector[index] += (hash[4] & 1) == 0 ? 1f : -1f;
        }
        return EmbeddingBytes.Normalise(vector);
    }

    // Words and identifiers: "NC-450" and "E14" stay whole.
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*")]
    private static partial Regex WordPattern();
}
