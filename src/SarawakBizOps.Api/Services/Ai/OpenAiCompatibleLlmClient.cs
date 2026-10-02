using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// A real model behind the OpenAI-compatible <c>/chat/completions</c> API (Open Question 1, decided in Phase 13), hand
/// rolled over <see cref="HttpClient"/> with no SDK package (PRD D-06). One client reaches many providers by
/// configuration alone: Google Gemini (its free tier, through the OpenAI-compatible endpoint), Groq, a local Ollama,
/// OpenAI or Azure OpenAI. Never used by the tests or CI, which run on <see cref="MockLlmClient"/>.
/// </summary>
public class OpenAiCompatibleLlmClient : ILlmClient
{
    /// <summary>A provider's reply is read up to this size; anything larger is refused unread.</summary>
    public const int MaxResponseBytes = 256 * 1024;

    private readonly HttpClient _http;
    private readonly AiOptions _options;

    public OpenAiCompatibleLlmClient(HttpClient http, IOptions<AiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string DisplayName => _options.Model ?? "AI model";

    public async Task<string> CompleteStructuredAsync(LlmRequest request, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["temperature"] = 0.2,
            ["max_tokens"] = _options.MaxOutputTokens,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    // The schema is in the prompt too, for servers that only offer plain JSON mode.
                    ["content"] = request.SystemPrompt + "\n\nJSON schema of the answer:\n" + request.Schema.ToJsonString()
                },
                new JsonObject { ["role"] = "user", ["content"] = request.UserContent }),
            ["response_format"] = string.Equals(_options.ResponseFormat, "json_object", StringComparison.OrdinalIgnoreCase)
                ? new JsonObject { ["type"] = "json_object" }
                : new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = request.SchemaName,
                        ["strict"] = true,
                        ["schema"] = request.Schema.DeepClone()
                    }
                }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, CompletionsUri(_options.BaseUrl!))
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
            throw new LlmProviderException($"The provider answered HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new LlmProviderException("The provider's reply was too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var limited = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (limited.Length + read > MaxResponseBytes)
            {
                throw new LlmProviderException("The provider's reply was too large.");
            }
            limited.Write(buffer, 0, read);
        }

        return ReadContent(limited.ToArray());
    }

    public static Uri CompletionsUri(string baseUrl) => new(baseUrl.TrimEnd('/') + "/chat/completions");

    /// <summary>The first choice's message content. A reply cut off by the token limit is refused: its JSON would
    /// be incomplete anyway.</summary>
    public static string ReadContent(byte[] reply)
    {
        try
        {
            using var document = JsonDocument.Parse(reply);
            var choice = document.RootElement.GetProperty("choices")[0];
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String
                && finish.GetString() == "length")
            {
                throw new LlmProviderException("The provider's answer was cut off at the token limit.");
            }

            return choice.GetProperty("message").GetProperty("content").GetString()
                ?? throw new LlmProviderException("The provider's answer had no content.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new LlmProviderException("The provider's reply was not a chat completion.");
        }
    }
}

/// <summary>A provider failure described in our own words (safe to log: no prompt, answer or key in it).</summary>
public class LlmProviderException : Exception
{
    public LlmProviderException(string message) : base(message) { }
}
