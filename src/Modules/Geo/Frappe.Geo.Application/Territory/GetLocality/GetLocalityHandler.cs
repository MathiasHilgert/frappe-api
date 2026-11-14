using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.GetLocality;

internal sealed class GetLocalityHandler(ITerritoryDirectory territory) : IQueryHandler<GetLocality, LocalityView>
{
    public async Task<Result<LocalityView>> HandleAsync(GetLocality query, CancellationToken cancellationToken) =>
        PublicId.TryParse(query.LocalityId, out var country, out var id) && await territory.FindLocalityAsync(country, id, cancellationToken) is { } locality
            ? locality
            : GeoErrors.LocalityNotFound(query.LocalityId);
}
