using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.GetArea;

internal sealed class GetAreaHandler(ITerritoryDirectory territory) : IQueryHandler<GetArea, AdministrativeAreaView>
{
    public async Task<Result<AdministrativeAreaView>> HandleAsync(GetArea query, CancellationToken cancellationToken) =>
        PublicId.TryParse(query.AreaId, out var country, out var id) && await territory.FindAreaAsync(country, id, cancellationToken) is { } area
            ? area
            : GeoErrors.AreaNotFound(query.AreaId);
}
