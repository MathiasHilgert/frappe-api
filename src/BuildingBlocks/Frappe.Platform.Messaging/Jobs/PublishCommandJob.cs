using Quartz;
using Wolverine;

namespace Frappe.Platform.Messaging.Jobs;

/// <summary>
/// Recurring jobs carry no logic: on each trigger they publish a Wolverine command, so retries,
/// dead-lettering and idempotency live in one place.
/// </summary>
[DisallowConcurrentExecution]
public sealed class PublishCommandJob<TCommand>(IMessageBus bus) : IJob
    where TCommand : new()
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        await bus.PublishAsync(new TCommand());
}
