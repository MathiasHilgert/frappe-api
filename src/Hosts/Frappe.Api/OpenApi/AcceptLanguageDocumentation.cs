using Frappe.Http;
using Frappe.Localization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Api.OpenApi;

/// <summary>
/// Declares the <c>Accept-Language</c> header on every operation that negotiates the language, and the
/// <c>Content-Language</c> of its successful answer.
/// </summary>
internal sealed class AcceptLanguageDocumentation : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<NegotiatesRequestLanguageMetadata>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept-Language",
            In = ParameterLocation.Header,
            Required = false,
            Description = "The languages the client accepts (BCP 47, with q weights). Frappé is written in "
                + string.Join(", ", LanguageFallback.Supported) + "; the ranges are tried from the highest quality down and "
                + "the first one served wins (fr, es-AR;q=0.8 answers in es-AR). Another Spanish answers in es, any "
                + "Portuguese in pt-BR, and anything else, or no header, in English. Content-Language names the language served.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });

        if (operation.Responses?.TryGetValue("200", out var found) == true && found is OpenApiResponse ok)
        {
            ok.Headers ??= new Dictionary<string, IOpenApiHeader>();
            ok.Headers["Content-Language"] = new OpenApiHeader
            {
                Description = "The languages Frappé's own texts in the response are written in; absent when it holds none, only "
                    + "proper nouns, identifiers and source data (names and categories as their source publishes them).",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };
        }

        return Task.CompletedTask;
    }
}
