using Quartz;

namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// The daily retention (S16): every <see cref="IRetentionStep"/> in registration order (the sweep that ends expired sessions, then the purges
/// of stale rows), then the module's published outbox rows and inbox records. A failure of one step does not skip the next; the job fails
/// with the first error so the scheduler records it.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class IdentityRetentionJob(IEnumerable<IRetentionStep> steps, IEnumerable<IMessagePurge> messages) : IJob
{
    /// <summary>The job's key.</summary>
    public static readonly JobKey Key = new("retention", "identity");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        Exception? failure = null;
        foreach (var step in steps)
        {
            try
            {
                await step.RunAsync(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure ??= exception;
            }
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
