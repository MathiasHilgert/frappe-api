using Frappe.Geo.Application.Countries;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Countries.GetCountry;

internal static class GetCountryEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/countries/{countryCode}", Get)
            .AllowAnonymous()
            .WithName("GetCountry")
            .WithSummary(
                "Gets one ISO 3166-1 country by its alpha-2 code, with its name in the requested locale and its address format."
            )
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<CountryDetailDto>, ProblemHttpResult> Get(
        string countryCode,
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

        // A malformed code in the path names no resource, so it is not found like an unknown one.
        ErrorOr<CountryCode> code = CountryCode.Create(countryCode);
        if ((code.IsError ? null : countries.Find(code.Value, requested.Value)) is not { } country)
        {
            return new[] { CountryErrors.NotFound }.ToProblem(context);
        }

        BodyLocale.Served(context, country.Locale);
        return TypedResults.Ok(country.Value);
    }
}
