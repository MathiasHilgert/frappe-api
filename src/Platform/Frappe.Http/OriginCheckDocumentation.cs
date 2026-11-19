using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Http;

/// <summary>
/// Declares the <c>403</c> of the <c>Origin</c> check on every unsafe operation
/// (<c>POST</c>, <c>PUT</c>, <c>PATCH</c>, <c>DELETE</c>), in one place: whatever the route, a request that carries the
/// session cookie without an allowed <c>Origin</c> is answered <c>403</c> <c>http.origin.forbidden</c>
/// before it runs. An operation that declares a <c>403</c> of its own keeps it, with the Origin refusal added to its
/// description. Registered after <see cref="ProblemDocumentation"/>, whose schema the answer refers to.
/// </summary>
internal sealed class OriginCheckDocumentation : IOpenApiDocumentTransformer
{
    private const string OriginForbiddenCode = "http.origin.forbidden";

    private static readonly HttpMethod[] Unsafe = [HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var path in document.Paths.Values.OfType<OpenApiPathItem>())
        {
            foreach (var (_, operation) in (path.Operations ?? []).Where(operation => Unsafe.Contains(operation.Key)))
            {
                operation.Responses ??= [];
                const string Refusal = $"Forbidden: {OriginForbiddenCode} when the request carries the session cookie without an allowed Origin "
                    + "(a bearer token is never checked).";
                if (operation.Responses.TryGetValue("403", out var declared) && declared is OpenApiResponse own)
                {
                    // The operation's own 403 stays, as a problem: the Origin refusal is one more reason for it.
                    own.Description = string.IsNullOrWhiteSpace(own.Description) ? Refusal : $"{own.Description}\n\n{Refusal}";
                    ProblemDocumentation.Describe(own, "403", document);
                    continue;
                }

                var forbidden = new OpenApiResponse { Description = Refusal };
                ProblemDocumentation.Describe(forbidden, "403", document);
                operation.Responses["403"] = forbidden;
            }
        }

        return Task.CompletedTask;
    }
}
