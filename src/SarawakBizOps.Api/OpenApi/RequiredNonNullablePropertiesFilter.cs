using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SarawakBizOps.Api.OpenApi;

/// <summary>
/// Says which properties are always in the JSON, so the TypeScript types generated from the document are exact.
/// The API writes every property of what it returns, a null one as <c>null</c>: every property of a response is
/// required (nullable or not). A request (<c>*Request</c>) may leave out what can be null.
/// </summary>
public sealed class RequiredNonNullablePropertiesFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var isRequest = context.Type.Name.EndsWith("Request", StringComparison.Ordinal);

        foreach (var (name, property) in schema.Properties)
        {
            if (!isRequest || !property.Nullable)
            {
                schema.Required.Add(name);
            }
        }
    }
}
