using Frappe.Geo.Application;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Subdivisions.ListSubdivisions;

internal static class ListSubdivisionsEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/countries/{countryCode}/subdivisions", List)
            .AllowAnonymous()
            .WithName("ListSubdivisions")
            .WithSummary(
                "Lists the ISO 3166-2 subdivisions of a country, such as the Argentine provinces, by their name in the requested locale."
            )
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<PageResponse<SubdivisionDto>>, ProblemHttpResult> List(
        string countryCode,
        string? locale,
        ICountryReader countries,
        ISubdivisionReader subdivisions,
        HttpContext context
    )
    {
        ErrorOr<Locale> requested = BodyLocale.Of(locale);
        if (requested.IsError)
        {
            return requested.Errors.ToProblem(context);
        }

        // A malformed code in the path names no resource, so it is not found like an unknown one.
        ErrorOr<CountryCode> code = CountryCode.Create(countryCode);
        if (code.IsError || countries.Find(code.Value, Locale.English) is null)
        {
            return new[] { CountryErrors.NotFound }.ToProblem(context);
        }

        Localized<IReadOnlyList<SubdivisionDto>> list = subdivisions.List(
            code.Value,
            requested.Value
        );
        BodyLocale.Served(context, list.Locale);
        return TypedResults.Ok(new PageResponse<SubdivisionDto>(list.Value, NextCursor: null));
    }
}
