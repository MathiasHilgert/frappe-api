using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.TimeZones;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Kernel;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.TimeZones.ListTimeZones;

internal static class ListTimeZonesEndpoint
{
    private const string CountryPointer = "/countryCode";

    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/time-zones", List)
            .AllowAnonymous()
            .WithName("ListTimeZones")
            .WithSummary(
                "Lists the IANA time zones a branch can run in, optionally of one country."
            )
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<PageResponse<TimeZoneDto>>, ProblemHttpResult> List(
        string? countryCode,
        ITimeZoneReader timeZones,
        ICountryReader countries,
        HttpContext context
    )
    {
        if (countryCode is null)
        {
            return TypedResults.Ok(
                new PageResponse<TimeZoneDto>(timeZones.List(country: null), NextCursor: null)
            );
        }

        ErrorOr<CountryCode> country = CountryCode.Create(countryCode).WithPointer(CountryPointer);
        if (country.IsError)
        {
            return country.Errors.ToProblem(context);
        }

        return countries.Find(country.Value, Locale.English) is null
            ? new[] { UnknownFilter.Of(CountryErrors.NotFound, CountryPointer) }.ToProblem(context)
            : TypedResults.Ok(
                new PageResponse<TimeZoneDto>(timeZones.List(country.Value), NextCursor: null)
            );
    }
}
