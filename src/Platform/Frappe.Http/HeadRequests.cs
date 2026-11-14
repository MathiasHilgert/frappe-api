using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Http;

/// <summary>
/// HEAD for the routes that opt in (RFC 9110 §9.3.2): the same status and headers as GET, no content. A GET endpoint
/// answers HEAD only once <see cref="AnswerHead"/> was applied to it or to its group; it then declares HEAD next to GET
/// in its <see cref="HttpMethodMetadata"/>, so routing matches it and lists it in a <c>405</c>'s <c>Allow</c>, and the
/// endpoint runs as the HEAD that was asked. An endpoint mapped without methods (the health probes) answers HEAD
/// already. The OpenAPI document drops the HEAD operations, keeping one GET per route.
/// </summary>
public static class HeadRequests
{
    /// <summary>Adds HEAD to every endpoint of <paramref name="builder"/> that answers GET.</summary>
    public static TBuilder AnswerHead<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(endpoint =>
        {
            // Routing reads the last method metadata, so the wider one is added after the endpoint's own.
            if (endpoint.Metadata.OfType<IHttpMethodMetadata>().LastOrDefault() is { } methods
                && methods.HttpMethods.Any(HttpMethods.IsGet)
                && !methods.HttpMethods.Any(HttpMethods.IsHead))
            {
                endpoint.Metadata.Add(new HttpMethodMetadata([.. methods.HttpMethods, HttpMethods.Head], methods.AcceptCorsPreflight));
            }
        });
        return builder;
    }

    /// <summary>
    /// Discards what a HEAD response writes, as Kestrel does, so every server (the test server included) answers HEAD
    /// without content while the headers, <c>Content-Length</c> among them, stay those of GET.
    /// </summary>
    internal static async Task DiscardBodyAsync(HttpContext httpContext, RequestDelegate next)
    {
        if (!HttpMethods.IsHead(httpContext.Request.Method))
        {
            await next(httpContext);
            return;
        }

        var body = httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>();
        httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null, body));
        try
        {
            await next(httpContext);
        }
        finally
        {
            httpContext.Features.Set(body);
        }
    }
}
