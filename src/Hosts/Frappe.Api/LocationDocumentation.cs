using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Api;

/// <summary>
/// Declares the <c>Location</c> header of every <c>201 Created</c> (the new resource) and <c>202 Accepted</c> (where to
/// follow the accepted request), which the generator leaves out.
/// </summary>
internal sealed class LocationDocumentation : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var (status, response) in operation.Responses ?? [])
        {
            if (status is not ("201" or "202") || response is not OpenApiResponse declared)
            {
                continue;
            }

            declared.Headers ??= new Dictionary<string, IOpenApiHeader>();
            declared.Headers["Location"] = new OpenApiHeader
            {
                Description = status == "201" ? "The URL of the resource created." : "The URL that follows the accepted request.",
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" },
            };
        }

        return Task.CompletedTask;
    }
}
