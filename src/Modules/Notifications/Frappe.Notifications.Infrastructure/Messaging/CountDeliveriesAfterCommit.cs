using Frappe.IntegrationEvents;
using Frappe.Notifications.Application.Deliveries;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// Counts the deliveries a consumer's dispatch decided on once its claim committed, with the delivery rows (OBS-06). The dispatch runs
/// before the claim, so it only records what it decided in <see cref="PendingDeliveries"/>. It runs on a redelivered message and on a lost claim
/// too, where nothing marked the scope claimed, so neither counts twice. Best effort like every after-commit step (OBS-10).
/// </summary>
internal sealed class CountDeliveriesAfterCommit<TEvent>(PendingDeliveries pending) : IAfterCommitHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken)
    {
        pending.CountIfClaimed();
        return Task.CompletedTask;
    }
}
