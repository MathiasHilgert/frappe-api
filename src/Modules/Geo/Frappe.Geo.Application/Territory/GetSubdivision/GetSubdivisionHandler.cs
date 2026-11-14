using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Territory;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.GetSubdivision;

internal sealed class GetSubdivisionHandler(ICountryCatalog countries, ITerritoryDirectory territory) : IQueryHandler<GetSubdivision, SubdivisionView>
{
    public async Task<Result<SubdivisionView>> HandleAsync(GetSubdivision query, CancellationToken cancellationToken)
    {
        var code = SubdivisionCode.Create(query.SubdivisionCode);
        if (!code.IsSuccess)
        {
            return Result.Failure<SubdivisionView>(code.Errors);
        }

        return await territory.FindSubdivisionAsync(code.Value, cancellationToken) is { } subdivision
            ? subdivision
            : await Subdivisions.MissingAsync(code.Value, countries, territory, cancellationToken);
    }
}
