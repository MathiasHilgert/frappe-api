using Microsoft.Extensions.Hosting;
using Quartz;

namespace Frappe.Platform.Messaging.Jobs;

/// <summary>
/// Stores every declared <see cref="RecurringJob"/> before the scheduler starts, without disturbing what the store
/// already knows. A trigger whose schedule is unchanged is left alone, so a restart or a deploy never resets its next
/// fire time or misfire state; a changed schedule replaces it in one store operation; a missing one is added, and of
/// two instances starting together exactly one stores it. Every trigger fires once on recovery after a misfire (all
/// instances were down at the scheduled time): recurring jobs publish idempotent commands, so one catch-up is right
/// and replaying each missed firing is not. A <see cref="PublishCommandJob{TCommand}"/> no module declares any more
/// (renamed or removed, or stored under another key by an older registration) is deleted with its triggers, so it
/// can never fire a command nobody expects; deleting is idempotent, and jobs of any other type are never touched.
/// </summary>
internal sealed class RecurringJobScheduler(ISchedulerFactory schedulers, IEnumerable<RecurringJob> jobs) : IHostedService
{
    private static readonly string PublishCommandJobName = typeof(PublishCommandJob<>).FullName!;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        var declared = jobs.ToList();
        foreach (var job in declared)
            await StoreAsync(scheduler, job, cancellationToken);
        await DeleteUndeclaredAsync(scheduler, [.. declared.Select(j => j.Job.Key)], cancellationToken);
    }

    private static async Task DeleteUndeclaredAsync(
        IScheduler scheduler, HashSet<JobKey> declared, CancellationToken cancellationToken)
    {
        foreach (var key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken))
        {
            if (declared.Contains(key))
                continue;
            var detail = await scheduler.GetJobDetail(key, cancellationToken);
            // By the stored name, so a job whose command type no longer exists (the usual stale case) still matches.
            if (detail?.JobType.FullName?.StartsWith(PublishCommandJobName, StringComparison.Ordinal) == true)
                await scheduler.DeleteJob(key, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task StoreAsync(IScheduler scheduler, RecurringJob job, CancellationToken cancellationToken)
    {
        var jobKey = job.Job.Key;
        // The job detail holds no state, so it is always rewritten (its type may have moved).
        await scheduler.AddJob(job.Job, AddJobOptions.Replacing, cancellationToken);

        var desired = (ICronTrigger)TriggerBuilder.Create()
            .WithIdentity(new TriggerKey(jobKey.Name))
            .ForJob(jobKey)
            .WithCronSchedule(job.CronExpression, cron => cron
                .InTimeZone(job.TimeZone)
                .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
            .Build();

        var stored = await scheduler.GetTrigger(desired.Key, cancellationToken);
        if (stored is null)
            await scheduler.ScheduleTrigger(desired, TriggerConflict.Keep, cancellationToken);
        else if (!IsSameSchedule(stored, desired))
            await scheduler.ScheduleJob(desired, ScheduleJobOptions.Replacing, cancellationToken);
    }

    private static bool IsSameSchedule(ITrigger stored, ICronTrigger desired) =>
        stored is ICronTrigger cron
        && cron.JobKey.Equals(desired.JobKey)
        && string.Equals(cron.CronExpressionString, desired.CronExpressionString, StringComparison.Ordinal)
        && string.Equals(cron.TimeZone.Id, desired.TimeZone.Id, StringComparison.Ordinal)
        && cron.MisfireInstruction == desired.MisfireInstruction;
}
