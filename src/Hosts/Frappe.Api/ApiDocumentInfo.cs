using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Api;

/// <summary>The document's <c>info</c>: what the API is, for whom, and its version.</summary>
internal sealed class ApiDocumentInfo : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Frappé API",
            Version = "1.0.0",
            Description = "The operating system for restaurants and cafés in Latin America. Version 1 is anonymous and "
                + "rate limited per client; its administration writes are not authenticated yet. Texts follow Accept-Language "
                + "and Content-Language names the languages served; every endpoint declares its caching, and cacheable "
                + "answers carry strong ETags; every error is an RFC 9457 Problem Details with a stable code.",
        };
        return Task.CompletedTask;
    }
}
