using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Template.WebApi.Infrastructure.Extensions;

/// <summary>
/// Documents the Phase 25+ procurement enums as strings, matching the wire format (docs/product/api-guidelines.md §2:
/// enums as strings). It is limited to the procurement Domain modules so legacy schemas such as <c>ErrorCode</c>
/// keep their existing integer representation.
/// </summary>
public sealed class StringEnumSchemaFilter : ISchemaFilter
{
    private static readonly string[] Namespaces = ["Template.Domain.Catalog", "Template.Domain.Suppliers", "Template.Domain.Organization"];

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || !context.Type.IsEnum || !Namespaces.Contains(context.Type.Namespace))
            return;

        concrete.Type = JsonSchemaType.String;
        concrete.Format = null;
        concrete.Enum = Enum.GetNames(context.Type).Select(name => (JsonNode)JsonValue.Create(name)!).ToList();
    }
}
