using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Application.Currencies.GetCurrency;
using Frappe.Geo.Application.Currencies.ListCountryCurrencies;
using Frappe.Geo.Application.Currencies.ListCurrencies;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Currencies;

/// <summary>The ISO 4217 currencies some country uses today.</summary>
internal static class CurrencyEndpoints
{
    private const string NamesInLanguage =
        "Name and symbol answer together in the language of Accept-Language: the requested tag, then its base language, "
        + "any Portuguese in pt-BR, then English. Content-Language names the languages served.";

    public static void MapCurrencyEndpoints(this RouteGroupBuilder geo)
    {
        geo.MapGet("/currencies", ListCurrenciesAsync)
            .WithName("ListCurrencies")
            .CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("Currencies in use")
            .WithDescription("Every ISO 4217 currency some country uses today as legal tender, ordered by code. " + NamesInLanguage);

        geo.MapGet("/currencies/{currencyCode}", GetCurrencyAsync)
            .WithName("GetCurrency")
            .CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("One currency")
            .WithDescription("A currency in use by its ISO 4217 code (case-insensitive). " + NamesInLanguage)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/countries/{countryCode}/currencies", ListCountryCurrenciesAsync)
            .WithName("ListCountryCurrencies")
            .CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("Currencies of a country")
            .WithDescription("The currencies a country uses today as legal tender, in order of preference. " + NamesInLanguage)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<CurrencyListResponse>, ErrorsProblemHttpResult>> ListCurrenciesAsync(
        HttpContext httpContext, IQueryHandler<ListCurrencies, IReadOnlyList<CurrencySummary>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCurrencies(ServedLanguages.LanguageOf(httpContext)), cancellationToken);
        return result.ToTypedHttpResult(currencies => ToList(httpContext, currencies));
    }

    private static async Task<Results<Ok<CurrencyResponse>, ErrorsProblemHttpResult>> GetCurrencyAsync(
        string currencyCode, HttpContext httpContext, IQueryHandler<GetCurrency, CurrencySummary> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCurrency(currencyCode, ServedLanguages.LanguageOf(httpContext)), cancellationToken);
        return result.ToTypedHttpResult(currency =>
        {
            ServedLanguages.Declare(httpContext, [currency.Language]);
            return TypedResults.Ok(ToResponse(currency));
        });
    }

    private static async Task<Results<Ok<CurrencyListResponse>, ErrorsProblemHttpResult>> ListCountryCurrenciesAsync(
        string countryCode, HttpContext httpContext, IQueryHandler<ListCountryCurrencies, IReadOnlyList<CurrencySummary>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCountryCurrencies(countryCode, ServedLanguages.LanguageOf(httpContext)), cancellationToken);
        return result.ToTypedHttpResult(currencies => ToList(httpContext, currencies));
    }

    private static Ok<CurrencyListResponse> ToList(HttpContext httpContext, IReadOnlyList<CurrencySummary> currencies)
    {
        ServedLanguages.Declare(httpContext, currencies.Select(c => c.Language));
        return TypedResults.Ok(new CurrencyListResponse([.. currencies.Select(ToResponse)]));
    }

    private static CurrencyResponse ToResponse(CurrencySummary currency) =>
        new(currency.Code, currency.Name, currency.Symbol, currency.MinorUnitDigits);
}
