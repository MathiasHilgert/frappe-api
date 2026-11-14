using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.RequestHolidaySync;

internal sealed class RequestHolidaySyncHandler(IHolidaySyncs syncs) : ICommandHandler<RequestHolidaySync, SyncRequestView>
{
    public async Task<Result<SyncRequestView>> HandleAsync(RequestHolidaySync command, CancellationToken cancellationToken) =>
        await syncs.ExistsAsync(command.SourceId, cancellationToken)
            ? syncs.RequestRun(command.SourceId)
            : GeoErrors.HolidaySyncNotFound(command.SourceId);
}
