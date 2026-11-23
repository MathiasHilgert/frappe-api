using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Frappe.Http.OpenApi;

/// <summary>The document's <c>info</c>: what the API is, for whom, and its version, as the host configured it.</summary>
internal sealed class ApiDocumentInfo(IOptions<ApiDocumentOptions> options) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var info = options.Value;
        document.Info = new OpenApiInfo { Title = info.Title, Version = info.Version, Description = info.Description };
        return Task.CompletedTask;
    }
}
