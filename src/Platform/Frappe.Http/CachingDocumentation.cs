using System.Globalization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Http;

/// <summary>
/// Declares the HTTP caching of each operation by the headers it sends, from the endpoint's caching metadata: for a
/// cacheable one (<see cref="CacheableMetadata"/>, with its <c>MaxAge</c>) <c>ETag</c> and
/// <c>Cache-Control</c> and <c>Vary</c> on <c>200</c>, the <c>If-None-Match</c> request header and the <c>304</c> it can
/// answer, which repeats them (RFC 9110 §15.4.5); for a <see cref="NoCacheMetadata"/> one the same, with
/// <c>Cache-Control: no-cache</c> (every use revalidates); for a <see cref="NoStoreMetadata"/> one,
/// <c>Cache-Control: no-store</c> on every success.
/// </summary>
internal sealed class CachingDocumentation : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<NoStoreMetadata>().Any())
        {
            foreach (var response in (operation.Responses ?? []).Where(r => r.Key[0] == '2').Select(r => r.Value).OfType<OpenApiResponse>())
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                response.Headers["Cache-Control"] = new OpenApiHeader
                {
                    Description = "no-store: no cache may keep the answer, and it carries no validator.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                };
            }
        }

        Func<OpenApiHeader> cacheControl;
        if (metadata.OfType<CacheableMetadata>().FirstOrDefault() is { } cacheable)
        {
            var maxAge = ((long)cacheable.MaxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            cacheControl = () => CacheControl(maxAge);
        }
        else if (metadata.OfType<NoCacheMetadata>().Any())
        {
            cacheControl = NoCache;
        }
        else
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-None-Match",
            In = ParameterLocation.Header,
            Required = false,
            Description = "ETags of the copies the client holds (or *): when one matches, the answer is 304 without a body.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });

        operation.Responses ??= [];
        if (operation.Responses.TryGetValue("200", out var found) && found is OpenApiResponse ok)
        {
            ok.Headers ??= new Dictionary<string, IOpenApiHeader>();
            ok.Headers["ETag"] = ETag();
            ok.Headers["Cache-Control"] = cacheControl();
            ok.Headers["Vary"] = Vary();
        }

        operation.Responses["304"] = new OpenApiResponse
        {
            Description = "Not Modified: If-None-Match matched the current ETag, so the client's copy is still current.",
            Headers = new Dictionary<string, IOpenApiHeader> { ["ETag"] = ETag(), ["Cache-Control"] = cacheControl(), ["Vary"] = Vary() },
        };

        return Task.CompletedTask;
    }

    private static OpenApiHeader ETag() => new()
    {
        Description = "Strong validator of this representation, language included; send it back in If-None-Match.",
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };

    private static OpenApiHeader Vary() => new()
    {
        Description = "Accept-Language, Origin: each language is a separate representation with its own ETag, and CORS headers depend on Origin; a cache keys on both.",
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };

    private static OpenApiHeader NoCache() => new()
    {
        Description = "no-cache: a cache may keep the answer but must revalidate it with If-None-Match before every use, so a change shows at once and an unchanged answer costs a 304.",
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };

    private static OpenApiHeader CacheControl(string maxAge) => new()
    {
        Description = $"public, max-age={maxAge} at most (seconds); some answers expire sooner, such as time zones before an offset change.",
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };
}
