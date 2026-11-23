using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Http.OpenApi;

/// <summary>
/// Declares the <c>403</c> of the <c>Origin</c> check (<see cref="ConsoleOriginCheck"/>) on every unsafe operation
/// (<c>POST</c>, <c>PUT</c>, <c>PATCH</c>, <c>DELETE</c>), in one place: whatever the route, a request that carries the
/// session cookie without a console <c>Origin</c> is answered <c>403</c> <see cref="HttpRegistration.OriginForbiddenCode"/>
/// before it runs. Registered after <see cref="ProblemDocumentation"/>, whose schema the answer refers to.
/// </summary>
internal sealed class OriginCheckDocumentation : IOpenApiDocumentTransformer
{
    private static readonly HttpMethod[] Unsafe = [HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var path in document.Paths.Values.OfType<OpenApiPathItem>())
        {
            foreach (var (_, operation) in (path.Operations ?? []).Where(operation => Unsafe.Contains(operation.Key)))
            {
                operation.Responses ??= [];
                var forbidden = new OpenApiResponse
                {
                    Description = $"Forbidden: {HttpRegistration.OriginForbiddenCode} when the request carries the session cookie without an Origin "
                        + "among the console's (a bearer token is never checked).",
                };
                ProblemDocumentation.Describe(forbidden, "403", document);
                operation.Responses["403"] = forbidden;
            }
        }

        return Task.CompletedTask;
    }
}
