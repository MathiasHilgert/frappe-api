using Frappe.Geo.Domain.Currencies;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Currencies.GetCurrency;

internal sealed class GetCurrencyHandler(ICurrencyCatalog catalog) : IQueryHandler<GetCurrency, CurrencySummary>
{
    public async Task<Result<CurrencySummary>> HandleAsync(GetCurrency query, CancellationToken cancellationToken)
    {
        var code = CurrencyCode.Create(query.CurrencyCode);
        if (!code.IsSuccess)
        {
            return Result.Failure<CurrencySummary>(code.Errors);
        }

        return await catalog.FindAsync(code.Value, cancellationToken) is { } currency
            ? CurrencySummary.Serve(currency, query.Language)
            : GeoErrors.CurrencyNotFound(code.Value.Value);
    }
}
