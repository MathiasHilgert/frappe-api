using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// Documents <c>nextCursor</c> of a page of sessions as optional: the body omits it on the last page (HTTP-14), but the generated schema lists
/// every property of the record as required.
/// </summary>
internal sealed class MySessionListDocumentation : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(MySessionListResponse))
        {
            schema.Required?.Remove("nextCursor");
        }

        return Task.CompletedTask;
    }
}
