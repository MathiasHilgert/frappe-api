using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Countries.ListCountries;

internal sealed class ListCountriesHandler(ICountryCatalog catalog) : IQueryHandler<ListCountries, IReadOnlyList<CountrySummary>>
{
    public async Task<Result<IReadOnlyList<CountrySummary>>> HandleAsync(ListCountries query, CancellationToken cancellationToken)
    {
        var countries = await catalog.ListAsync(cancellationToken);
        return countries.Select(c => CountrySummary.Serve(c, query.Language)).ToArray();
    }
}
