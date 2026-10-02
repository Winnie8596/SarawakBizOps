using System.Text.Json.Nodes;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The one seam to a language model (SDD §14, PRD D-02): a prompt and a JSON schema in, the model's raw JSON text
/// out. Everything else — what goes into the prompt, and checking what comes back — belongs to the caller, so a
/// provider is swapped by configuration alone and no provider ever sees more than the caller chose to send.
/// </summary>
public interface ILlmClient
{
    /// <summary>What the UI shows as the source of an answer, e.g. "Demo mock assistant" or the model name.</summary>
    string DisplayName { get; }

    /// <summary>Returns the model's answer as JSON text (not yet validated). Throws on any provider failure; the
    /// caller turns that into a controlled "AI unavailable".</summary>
    Task<string> CompleteStructuredAsync(LlmRequest request, CancellationToken ct);
}

/// <param name="SystemPrompt">The instructions. Never contains user-entered text.</param>
/// <param name="UserContent">The data to work on, as a JSON document. Free text from users lives only here, as
/// JSON string values, and the system prompt tells the model to treat it as data.</param>
/// <param name="SchemaName">Names the answer's shape (also how the mock knows what to answer).</param>
/// <param name="Schema">JSON Schema of the expected answer.</param>
public record LlmRequest(string SystemPrompt, string UserContent, string SchemaName, JsonObject Schema);
