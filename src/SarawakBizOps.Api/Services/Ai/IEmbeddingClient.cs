using System.Buffers.Binary;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The one seam to an embedding model (AI-1, docs/ai/decisions.md G4): texts in, one vector per text out, in the same
/// order. Vectors are L2-normalised by the client, so cosine similarity is a plain dot product for every caller.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>Recorded on every chunk; a different model means re-embedding, never mixing vectors.</summary>
    string ModelName { get; }

    int Dimension { get; }

    /// <summary>Throws <see cref="LlmProviderException"/> on any provider failure.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>Vectors as stored in <c>AiDocumentChunk.Embedding</c>: little-endian float32, whatever the machine.</summary>
public static class EmbeddingBytes
{
    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), vector[i]);
        }
        return bytes;
    }

    public static float[] ToVector(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new ArgumentException("An embedding must be a whole number of float32 values.", nameof(bytes));
        }

        var vector = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }
        return vector;
    }

    /// <summary>Scales a vector to length 1 in place; an all-zero vector is left as it is.</summary>
    public static float[] Normalise(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += v * v;
        if (sum == 0) return vector;

        var scale = (float)(1 / Math.Sqrt(sum));
        for (var i = 0; i < vector.Length; i++) vector[i] *= scale;
        return vector;
    }
}
