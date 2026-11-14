using Quartz;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>Inbound adapter for the scheduler: starts the sync job of each source someone asked to run by hand.</summary>
[DisallowConcurrentExecution]
internal sealed class SyncRequestJob(SyncRequestDispatch dispatch) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        foreach (var job in await dispatch.TakeAsync(cancellationToken))
        {
            await context.Scheduler.TriggerJob(new JobKey(job, SnapshotSyncRegistration.JobGroup), cancellationToken);
        }
    }
}
