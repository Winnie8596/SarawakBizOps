namespace SarawakBizOps.Api.Models.Enums;

/// <summary>Whether a knowledge-base document may be retrieved (docs/ai/decisions.md G8, G15). Only
/// <see cref="Active"/> ever is; stored as text.</summary>
public enum AiDocumentStatus
{
    Active,
    /// <summary>Another document's <c>supersedes</c> names this one.</summary>
    Superseded,
    /// <summary>Its file is no longer in <c>knowledge-base/</c>.</summary>
    Removed
}
