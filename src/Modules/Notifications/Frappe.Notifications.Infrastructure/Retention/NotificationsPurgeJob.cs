using Quartz;

namespace Frappe.Notifications.Infrastructure.Retention;

/// <summary>
/// The nightly purge (S11, CFG-06): old deliveries and provider events by <see cref="NotificationsPurger"/>, then the module's published outbox
/// rows and inbox records. A failure of the first does not skip the second; the job fails with the first error so the scheduler records it.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class NotificationsPurgeJob(NotificationsPurger purger, IEnumerable<IMessagePurge> messages) : IJob
{
    /// <summary>The job's key.</summary>
    public static readonly JobKey Key = new("purge", "notifications");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        Exception? failure = null;
        try
        {
            await purger.PurgeAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            failure = exception;
        }

        foreach (var purge in messages)
        {
            try
            {
                await purge.PurgeAsync(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
