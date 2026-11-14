using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.ListHolidaySyncRuns;

internal sealed class ListHolidaySyncRunsHandler(IHolidaySyncs syncs) : IQueryHandler<ListHolidaySyncRuns, IReadOnlyList<SyncRunView>>
{
    public async Task<Result<IReadOnlyList<SyncRunView>>> HandleAsync(ListHolidaySyncRuns query, CancellationToken cancellationToken) =>
        await syncs.ExistsAsync(query.SourceId, cancellationToken)
            ? Result.Success(await syncs.RunsAsync(query.SourceId, ListHolidaySyncRuns.Limit, cancellationToken))
            : GeoErrors.HolidaySyncNotFound(query.SourceId);
}
