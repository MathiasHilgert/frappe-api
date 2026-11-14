using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Currencies.ListCountryCurrencies;

internal sealed class ListCountryCurrenciesHandler(ICountryCatalog countries, ICurrencyCatalog currencies)
    : IQueryHandler<ListCountryCurrencies, IReadOnlyList<CurrencySummary>>
{
    public async Task<Result<IReadOnlyList<CurrencySummary>>> HandleAsync(ListCountryCurrencies query, CancellationToken cancellationToken)
    {
        var code = CountryCode.Create(query.CountryCode);
        if (!code.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<CurrencySummary>>(code.Errors);
        }

        if (await countries.FindAsync(code.Value, cancellationToken) is not { } country)
        {
            return GeoErrors.CountryNotFound(code.Value.Value);
        }

        var used = await currencies.FindAllAsync(country.CurrencyCodes, cancellationToken);
        return used.Select(c => CurrencySummary.Serve(c, query.Language)).ToArray();
    }
}
