using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi;

namespace Frappe.Http;

/// <summary>Documents the <c>ETag</c> of an answer that is never cached (HTTP-12).</summary>
public static class OpenApiETag
{
    extension(RouteHandlerBuilder builder)
    {
        /// <summary>
        /// Declares on the endpoint's <c>200</c> a required <c>Cache-Control</c> (<c>no-store</c>) and a required strong <c>ETag</c>: a tag
        /// that names a version, not a cache validator. Each module words both descriptions for its own tag.
        /// </summary>
        /// <param name="etagDescription">What the <c>ETag</c> header says.</param>
        /// <param name="cacheControlDescription">What the <c>Cache-Control</c> header says.</param>
        public RouteHandlerBuilder SendsNoStoreETag(string etagDescription, string cacheControlDescription) =>
            builder.AddOpenApiOperationTransformer((operation, _, _) =>
            {
                if (operation.Responses?.GetValueOrDefault("200") is OpenApiResponse ok)
                {
                    ok.Headers ??= new Dictionary<string, IOpenApiHeader>();
                    ok.Headers["Cache-Control"] = new OpenApiHeader
                    {
                        Description = cacheControlDescription,
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    };
                    ok.Headers["ETag"] = new OpenApiHeader
                    {
                        Description = etagDescription,
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    };
                }

                return Task.CompletedTask;
            });
    }
}
