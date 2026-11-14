using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Countries.GetCountry;
using Frappe.Geo.Application.Countries.ListCountries;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Countries;

/// <summary>The ISO 3166-1 countries.</summary>
internal static class CountryEndpoints
{
    private const string NamesInLanguage =
        "Names answer in the language of Accept-Language: the requested tag, then its base language, any Portuguese in "
        + "pt-BR, then English. Content-Language names the languages served.";

    public static void MapCountryEndpoints(this RouteGroupBuilder geo)
    {
        geo.MapGet("/countries", ListCountriesAsync)
            .WithName("ListCountries")
            .CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("Countries")
            .WithDescription("Every ISO 3166-1 country, ordered by code, with its codes, its name and its currencies in use. " + NamesInLanguage);

        geo.MapGet("/countries/{countryCode}", GetCountryAsync)
            .WithName("GetCountry")
            .CacheFor(CacheLifetimes.Calendars)
            .WithSummary("One country")
            .WithDescription("A country by its ISO 3166-1 alpha-2 code (case-insensitive), with capabilities: the years of each holiday calendar Geo holds for it, the business-day policies it answers and its reference time zone. " + NamesInLanguage)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<CountryListResponse>, ErrorsProblemHttpResult>> ListCountriesAsync(
        HttpContext httpContext, IQueryHandler<ListCountries, IReadOnlyList<CountrySummary>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCountries(ServedLanguages.LanguageOf(httpContext)), cancellationToken);
        return result.ToTypedHttpResult(countries =>
        {
            ServedLanguages.Declare(httpContext, countries.Select(c => c.NameLanguage));
            return TypedResults.Ok(new CountryListResponse([.. countries.Select(ToResponse)]));
        });
    }

    private static async Task<Results<Ok<CountryDetailResponse>, ErrorsProblemHttpResult>> GetCountryAsync(
        string countryCode, HttpContext httpContext, IQueryHandler<GetCountry, CountryDetail> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCountry(countryCode, ServedLanguages.LanguageOf(httpContext)), cancellationToken);
        return result.ToTypedHttpResult(detail =>
        {
            var (country, capabilities) = (detail.Country, detail.Capabilities);
            ServedLanguages.Declare(httpContext, [country.NameLanguage]);
            return TypedResults.Ok(new CountryDetailResponse(
                country.Code,
                country.Alpha3,
                country.Numeric,
                country.Name,
                country.CurrencyCodes,
                new CapabilitiesResponse(
                    [.. capabilities.Holidays.Select(h => new HolidayCoverageResponse(h.Scope, h.Years))],
                    [.. capabilities.BusinessDayPolicies.Select(BusinessDayPolicies.NameOf)],
                    capabilities.TimeZone)));
        });
    }

    private static CountryResponse ToResponse(CountrySummary country) =>
        new(country.Code, country.Alpha3, country.Numeric, country.Name, country.CurrencyCodes);
}
