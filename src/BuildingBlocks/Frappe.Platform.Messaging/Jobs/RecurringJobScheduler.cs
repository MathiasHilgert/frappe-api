using Microsoft.Extensions.Hosting;
using Quartz;

namespace Frappe.Platform.Messaging.Jobs;

/// <summary>
/// Stores every declared <see cref="RecurringJob"/> before the scheduler starts, without disturbing what the store
/// already knows. A trigger whose schedule is unchanged is left alone, so a restart or a deploy never resets its next
/// fire time or misfire state; a changed schedule replaces it in one store operation; a missing one is added, and of
/// two instances starting together exactly one stores it. Every trigger fires once on recovery after a misfire (all
/// instances were down at the scheduled time): recurring jobs publish idempotent commands, so one catch-up is right
/// and replaying each missed firing is not.
/// </summary>
internal sealed class RecurringJobScheduler(ISchedulerFactory schedulers, IEnumerable<RecurringJob> jobs) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        foreach (var job in jobs)
            await StoreAsync(scheduler, job, cancellationToken);
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
