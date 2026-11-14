using Frappe.Geo.Application.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListSubdivisionAliases;

internal sealed class ListSubdivisionAliasesHandler(ICountryCatalog countries, ITerritoryDirectory territory, ISubdivisionAliases aliases)
    : IQueryHandler<ListSubdivisionAliases, IReadOnlyList<SubdivisionAliasView>>
{
    public async Task<Result<IReadOnlyList<SubdivisionAliasView>>> HandleAsync(ListSubdivisionAliases query, CancellationToken cancellationToken)
    {
        var subdivision = await Subdivisions.ResolveAsync(query.SubdivisionCode, countries, territory, cancellationToken);
        return subdivision.IsSuccess
            ? Result.Success(await aliases.ListAsync(subdivision.Value, cancellationToken))
            : Result.Failure<IReadOnlyList<SubdivisionAliasView>>(subdivision.Errors);
    }
}
