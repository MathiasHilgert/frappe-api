using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListSubdivisions;

internal sealed class ListSubdivisionsHandler(ICountryCatalog countries, ITerritoryDirectory territory) : IQueryHandler<ListSubdivisions, IReadOnlyList<SubdivisionView>>
{
    public async Task<Result<IReadOnlyList<SubdivisionView>>> HandleAsync(ListSubdivisions query, CancellationToken cancellationToken)
    {
        var code = CountryCode.Create(query.CountryCode);
        if (!code.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<SubdivisionView>>(code.Errors);
        }

        if (await countries.FindAsync(code.Value, cancellationToken) is null)
        {
            return GeoErrors.CountryNotFound(code.Value.Value);
        }

        // No subdivisions held means Geo has no territory data of the country, not that it has none (as holidays, S6).
        var subdivisions = await territory.ListSubdivisionsAsync(code.Value.Value, cancellationToken);
        return subdivisions.Count > 0 ? Result.Success(subdivisions) : GeoErrors.TerritoryNotCovered(code.Value.Value);
    }
}
