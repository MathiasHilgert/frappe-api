using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi;

namespace Frappe.Http;

/// <summary>Documents what a <c>PATCH</c> takes beyond its body: the merge patch format (<see cref="MergePatch"/>) and
/// <see cref="IfMatch"/>.</summary>
public static class OpenApiPatch
{
    extension(RouteHandlerBuilder builder)
    {
        /// <summary>Declares <c>Accept-Patch</c> (RFC 5789) on the endpoint's <c>200</c> and <c>415</c>: <paramref name="resource"/> is patched in
        /// JSON Merge Patch.</summary>
        /// <param name="resource">What is patched, as the description names it ("the profile", "the business").</param>
        public RouteHandlerBuilder DeclaresAcceptPatch(string resource) =>
            builder.AddOpenApiOperationTransformer((operation, _, _) =>
            {
                foreach (var status in MergePatchStatuses)
                {
                    if (operation.Responses?.GetValueOrDefault(status) is OpenApiResponse response)
                    {
                        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                        response.Headers[MergePatch.AcceptPatchHeader] = new OpenApiHeader
                        {
                            Description = $"{MergePatch.MediaType}: {resource} is changed with a JSON Merge Patch (RFC 7396).",
                            Required = true,
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        };
                    }
                }

                return Task.CompletedTask;
            });

        /// <summary>Declares the optional <c>If-Match</c> request header (<see cref="IfMatch"/>).</summary>
        /// <param name="description">Where the tags come from and what a mismatch answers.</param>
        public RouteHandlerBuilder DeclaresIfMatch(string description) =>
            builder.AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "If-Match",
                    In = ParameterLocation.Header,
                    Required = false,
                    Description = description,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
                return Task.CompletedTask;
            });
    }

    private static readonly string[] MergePatchStatuses = ["200", "415"];
}
