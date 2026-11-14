using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Currencies.ListCurrencies;

internal sealed class ListCurrenciesHandler(ICurrencyCatalog catalog) : IQueryHandler<ListCurrencies, IReadOnlyList<CurrencySummary>>
{
    public async Task<Result<IReadOnlyList<CurrencySummary>>> HandleAsync(ListCurrencies query, CancellationToken cancellationToken)
    {
        var currencies = await catalog.ListAsync(cancellationToken);
        return currencies.Select(c => CurrencySummary.Serve(c, query.Language)).ToArray();
    }
}
