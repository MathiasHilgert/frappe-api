using Frappe.Geo.Domain.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Countries.GetCountry;

internal sealed class GetCountryHandler(ICountryCatalog catalog)
    : IQueryHandler<GetCountry, CountrySummary>
{
    public async Task<Result<CountrySummary>> HandleAsync(GetCountry query, CancellationToken cancellationToken)
    {
        var code = CountryCode.Create(query.CountryCode);
        if (!code.IsSuccess)
        {
            return Result.Failure<CountrySummary>(code.Errors);
        }

        if (await catalog.FindAsync(code.Value, cancellationToken) is not { } country)
        {
            return GeoErrors.CountryNotFound(code.Value.Value);
        }

        return CountrySummary.Serve(country, query.Language);
    }
}
