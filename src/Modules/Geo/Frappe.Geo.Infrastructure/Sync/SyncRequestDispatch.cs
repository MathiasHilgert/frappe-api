using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>
/// Takes the pending run requests (removing them in one statement, so two Workers never take the same one) and says
/// which sync jobs to trigger: one per requested source, however many times it was asked for. The job then runs like a
/// scheduled one, never alongside another run of the same source. A request for a source this Worker does not compose
/// is dropped.
/// </summary>
internal sealed class SyncRequestDispatch(GeoDbContext db, IReadOnlyDictionary<string, string> jobBySource)
{
    public async Task<IReadOnlyList<string>> TakeAsync(CancellationToken cancellationToken)
    {
        var requested = await db.Database
            .SqlQuery<string>($"DELETE FROM geo.sync_requests RETURNING source_id AS \"Value\"")
            .ToListAsync(cancellationToken);
        return [.. requested.Distinct(StringComparer.Ordinal).Where(jobBySource.ContainsKey).Select(source => jobBySource[source])];
    }
}
