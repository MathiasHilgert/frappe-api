using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.ListHolidaySyncs;

internal sealed class ListHolidaySyncsHandler(IHolidaySyncs syncs) : IQueryHandler<ListHolidaySyncs, IReadOnlyList<HolidaySyncView>>
{
    public async Task<Result<IReadOnlyList<HolidaySyncView>>> HandleAsync(ListHolidaySyncs query, CancellationToken cancellationToken) =>
        Result.Success(await syncs.ListAsync(cancellationToken));
}
