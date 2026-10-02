namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The "Ai" configuration section (Phase 13, PRD §6.10, NFR-09). The provider, model and timeout come from
/// configuration; the key comes only from user-secrets or an environment variable (<c>Ai__ApiKey</c>) and is never
/// committed. With no section at all the app uses the deterministic mock, so development, tests and the Docker demo
/// need no key and cost nothing.
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary><see cref="AiProviders.Mock"/> (default) or <see cref="AiProviders.OpenAICompatible"/>.</summary>
    public string Provider { get; set; } = AiProviders.Mock;

    /// <summary>OpenAI-compatible only: the API root that <c>/chat/completions</c> is appended to, e.g.
    /// <c>https://generativelanguage.googleapis.com/v1beta/openai/</c> (Gemini), <c>https://api.groq.com/openai/v1</c>,
    /// <c>http://localhost:11434/v1</c> (Ollama) or <c>https://api.openai.com/v1</c>.</summary>
    public string? BaseUrl { get; set; }

    public string? Model { get; set; }

    /// <summary>Secret. user-secrets or <c>Ai__ApiKey</c> only; may be empty for a local server that needs none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>OpenAI-compatible only: <c>json_schema</c> (structured output, the default) or <c>json_object</c> for
    /// servers without schema support — the schema is in the prompt either way, and the answer is validated.</summary>
    public string ResponseFormat { get; set; } = "json_schema";

    /// <summary>How long one answer may take before the user is told the assistant is unavailable.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public int MaxOutputTokens { get; set; } = 1200;

    /// <summary>Requests per user per minute across the AI endpoints (a simple per-user rate limit).</summary>
    public int PermitsPerMinute { get; set; } = 10;
}

public static class AiProviders
{
    public const string Mock = "Mock";
    public const string OpenAICompatible = "OpenAICompatible";
}
