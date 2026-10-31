using Frappe.Geo.Application.Timezones.ListTimezones;
using Frappe.Kernel.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NodaTime.TimeZones;

namespace Frappe.Geo.Infrastructure.Timezones.ListTimezones;

/// <summary><c>GET /v1/geo/timezones</c>: every IANA time zone, anonymous and publicly cacheable.</summary>
internal static class ListTimezonesEndpoint
{
    // The catalogue is the TZDB release this build ships and the migrator seeded, so its version names the body.
    private static readonly EntityTagHeaderValue _etag = new(
        $"\"{TzdbDateTimeZoneSource.Default.TzdbVersion}\""
    );

    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/timezones", HandleAsync)
            .WithName("ListTimezones")
            .WithSummary("Lists every IANA time zone, ordered by id.")
            .AllowAnonymous();

    private static async Task<
        Results<Ok<ListTimezonesResponse>, StatusCodeHttpResult, ProblemHttpResult>
    > HandleAsync(
        HttpContext context,
        IDispatcher dispatcher,
        IOptions<GeoOptions> options,
        CancellationToken cancellationToken
    )
    {
        if (
            context
                .Request.GetTypedHeaders()
                .IfNoneMatch.Any(static tag =>
                    tag.Equals(EntityTagHeaderValue.Any)
                    || tag.Compare(_etag, useStrongComparison: false)
                )
        )
        {
            SetCacheHeaders(context, options.Value);
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        ErrorOr<IReadOnlyList<TimezoneDto>> result = await dispatcher.QueryAsync(
            new ListTimezonesQuery(),
            cancellationToken
        );
        // shortcut: the query has no expected error and Platform has no ErrorOr-to-Problem mapper yet; map through
        // it once it exists.
        if (result.IsError)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        SetCacheHeaders(context, options.Value);
        return TypedResults.Ok(new ListTimezonesResponse(result.Value, NextCursor: null));
    }

    // Only a 200 or a 304 is cacheable: an explicit max-age would let shared caches keep an error for a day.
    // No principal and no locale shape the body, so every cache may share it and no Vary is needed.
    private static void SetCacheHeaders(HttpContext context, GeoOptions options)
    {
        ResponseHeaders headers = context.Response.GetTypedHeaders();
        headers.ETag = _etag;
        headers.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = options.TimezonesMaxAge,
        };
    }
}
