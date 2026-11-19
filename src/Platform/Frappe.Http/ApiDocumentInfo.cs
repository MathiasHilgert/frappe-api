using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Frappe.Http;

/// <summary>The document's <c>info</c>: what the API is, for whom, and its version, as the host configured it (<see cref="ApiDocumentOptions"/>).</summary>
internal sealed class ApiDocumentInfo(IOptions<ApiDocumentOptions> options) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        document.Info = new OpenApiInfo
        {
            Title = configured.Title,
            Version = configured.Version,
            Description = configured.Description.Length == 0 ? null : configured.Description,
        };
        return Task.CompletedTask;
    }
}
