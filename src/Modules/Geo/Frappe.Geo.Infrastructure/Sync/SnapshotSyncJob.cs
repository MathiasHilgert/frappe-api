using Quartz;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>Inbound adapter for the scheduler: one run of a source's sync. It never runs twice at once.</summary>
[DisallowConcurrentExecution]
internal sealed class SnapshotSyncJob<TSnapshot, TSource>(SnapshotSync<TSnapshot, TSource> sync) : IJob
    where TSource : Frappe.Geo.Application.Sync.ISnapshotSource<TSnapshot>
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        await sync.RunAsync(cancellationToken);
}
