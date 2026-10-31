using Frappe.Geo.Application;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Countries.ListCountries;

internal static class ListCountriesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/countries", List)
            .AllowAnonymous()
            .WithName("ListCountries")
            .WithSummary("Lists the ISO 3166-1 countries, by their name in the requested locale.")
            .WithDescription(
                "Lists active countries only; a retired country is still served by GET /countries/{countryCode}."
            )
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<PageResponse<CountryDto>>, ProblemHttpResult> List(
        string? locale,
        ICountryReader countries,
        HttpContext context
    )
    {
        ErrorOr<Locale> requested = BodyLocale.Of(locale);
        if (requested.IsError)
        {
            return requested.Errors.ToProblem(context);
        }

        Localized<IReadOnlyList<CountryDto>> list = countries.List(requested.Value);
        BodyLocale.Served(context, list.Locale);
        return TypedResults.Ok(new PageResponse<CountryDto>(list.Value, NextCursor: null));
    }
}
