using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Api;

/// <summary>
/// Types every enum schema as a string: the API writes enums by name (<c>JsonStringEnumConverter</c>), but the
/// generated schema lists only the values, which leaves clients and validators guessing the type.
/// </summary>
internal sealed class StringEnumDocumentation : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type.IsEnum && schema.Enum is { Count: > 0 } && schema.Type is null)
        {
            schema.Type = JsonSchemaType.String;
        }

        return Task.CompletedTask;
    }
}
