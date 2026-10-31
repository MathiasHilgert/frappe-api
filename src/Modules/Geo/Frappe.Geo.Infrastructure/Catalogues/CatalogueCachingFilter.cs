using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>
/// Conditional GET for the catalogue reads. The dataset changes only with a deployment, so one tag covers every
/// response: the endpoint runs first, and only a 200 whose request already holds the tag becomes a 304, so a 404 or
/// 422 is never masked; a 200 is cacheable for a day, and anything else is never stored.
/// </summary>
/// <param name="catalogues">The loader, which fixes the dataset tag at startup.</param>
internal sealed class CatalogueCachingFilter(GeoCatalogueLoader catalogues) : IEndpointFilter
{
    private const int CacheMaxAge = 86_400;

    private static readonly string _cacheable = string.Create(
        CultureInfo.InvariantCulture,
        $"public, max-age={CacheMaxAge}"
    );

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        HttpContext http = context.HttpContext;
        object? result = await next(context);
        if (StatusOf(result) != StatusCodes.Status200OK)
        {
            http.Response.Headers.CacheControl = "no-store";
            return result;
        }

        EntityTagHeaderValue tag = catalogues.DatasetTag;
        http.Response.Headers.ETag = tag.ToString();
        http.Response.Headers.CacheControl = _cacheable;
        return Matches(http.Request.GetTypedHeaders().IfNoneMatch, tag)
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
            : result;
    }

    // The typed results of an endpoint, possibly wrapped in a Results<...> union.
    private static int? StatusOf(object? result) =>
        result switch
        {
            INestedHttpResult nested => StatusOf(nested.Result),
            IStatusCodeHttpResult status => status.StatusCode,
            _ => null,
        };

    // RFC 9110 If-None-Match: weak comparison against every listed tag, or * for the current representation.
    private static bool Matches(IList<EntityTagHeaderValue> tags, EntityTagHeaderValue current) =>
        tags.Any(tag =>
            tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false)
        );
}
