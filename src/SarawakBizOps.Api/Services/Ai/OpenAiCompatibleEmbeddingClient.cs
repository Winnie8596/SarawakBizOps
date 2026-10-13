using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// Embeddings through the OpenAI-compatible <c>/embeddings</c> API (AI-1, G4), with the same base URL and key as the
/// chat client, so Gemini, OpenAI or a local Ollama work by configuration alone. <c>dimensions</c> asks the provider
/// for <see cref="AiOptions.EmbeddingDimension"/> values; a reply of any other length is refused rather than stored.
/// Never used by the tests or CI, which run on <see cref="MockEmbeddingClient"/>.
/// </summary>
public class OpenAiCompatibleEmbeddingClient : IEmbeddingClient
{
    /// <summary>A batch of 32 vectors of 3,072 floats as JSON is well under this.</summary>
    public const int MaxResponseBytes = 8 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly AiOptions _options;

    public OpenAiCompatibleEmbeddingClient(HttpClient http, IOptions<AiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string ModelName => _options.EmbeddingModel!;

    public int Dimension => _options.EmbeddingDimension;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();

        var body = new JsonObject
        {
            ["model"] = _options.EmbeddingModel,
            ["input"] = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["dimensions"] = _options.EmbeddingDimension
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, EmbeddingsUri(_options.BaseUrl!))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Status only: a provider's error body is not ours to log.
            throw new LlmProviderException($"The embedding provider answered HTTP {(int)response.StatusCode}.");
        }
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new LlmProviderException("The embedding provider's reply was too large.");
        }

        var reply = await response.Content.ReadAsByteArrayAsync(ct);
        if (reply.Length > MaxResponseBytes)
        {
            throw new LlmProviderException("The embedding provider's reply was too large.");
        }
        return ReadVectors(reply, texts.Count, _options.EmbeddingDimension);
    }

    public static Uri EmbeddingsUri(string baseUrl) => new(baseUrl.TrimEnd('/') + "/embeddings");

    /// <summary>One normalised vector per input, in input order (by each item's <c>index</c>).</summary>
    public static IReadOnlyList<float[]> ReadVectors(byte[] reply, int expectedCount, int expectedDimension)
    {
        try
        {
            using var document = JsonDocument.Parse(reply);
            var data = document.RootElement.GetProperty("data");
            if (data.GetArrayLength() != expectedCount)
            {
                throw new LlmProviderException("The embedding provider returned the wrong number of vectors.");
            }

            var vectors = new float[expectedCount][];
            var position = 0;
            foreach (var item in data.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var i) ? i.GetInt32() : position;
                if (index < 0 || index >= expectedCount || vectors[index] is not null)
                {
                    throw new LlmProviderException("The embedding provider's reply had a bad index.");
                }

                var values = item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (values.Length != expectedDimension)
                {
                    throw new LlmProviderException(
                        $"The embedding provider returned {values.Length} dimensions; Ai:EmbeddingDimension is {expectedDimension}.");
                }
                vectors[index] = EmbeddingBytes.Normalise(values);
                position++;
            }
            return vectors;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new LlmProviderException("The embedding provider's reply was not an embeddings response.");
        }
    }
}
