using Frappe.Geo.Application.Timezones.ListTimezones;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Timezones.ListTimezones;

/// <summary><c>GET /v1/geo/timezones</c>: every IANA time zone, anonymous and publicly cacheable.</summary>
internal static class ListTimezonesEndpoint
{
    // shortcut: Platform has no rate limiter, anonymous-endpoint route test or request timeouts yet, so this
    // endpoint declares no `public` rate-limit policy, is on no declared anonymous list and has no timeout policy;
    // add each here when its Platform piece lands.
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet(
                "/timezones",
                static (
                    HttpContext context,
                    CatalogueSnapshot<ListTimezonesQuery, TimezoneDto> zones
                ) => zones.ServeAsync(context)
            )
            .WithName("ListTimezones")
            .WithSummary("Lists every IANA time zone, ordered by id.")
            .Produces<CataloguePage<TimezoneDto>>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AllowAnonymous();
}
