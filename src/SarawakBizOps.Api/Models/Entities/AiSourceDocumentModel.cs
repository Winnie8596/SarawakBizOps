namespace SarawakBizOps.Api.Models.Entities;

/// <summary>A model a manual applies to, stored normalised (<c>NORVIK</c> + <c>NC450</c>) so it matches
/// <c>Equipment.Brand</c>/<c>Model</c> however staff typed them (G3).</summary>
public class AiSourceDocumentModel
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public AiSourceDocument? Document { get; set; }

    public string NormalisedBrand { get; set; } = string.Empty;
    public string NormalisedModel { get; set; } = string.Empty;
}
