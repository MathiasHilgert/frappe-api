using Frappe.IntegrationEvents;
using Frappe.Notifications.Contracts;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// The dispatch consumer's step inside the claim transaction (Flow 3): for each delivery the before-claim step staged, it takes the lock of
/// the provider message (the webhook holds the same one around its lookup and parking) and applies what the provider reported meanwhile. The
/// lock lasts until the claim commits, so a webhook that finds no delivery has parked before this reads, and one that comes later waits and
/// finds the delivery. The claim saves the result with the deliveries.
/// </summary>
internal sealed class ApplyParkedFeedback(ParkedFeedbackApplier applier) : IIntegrationEventHandler<NotificationRequested>
{
    public Task HandleAsync(NotificationRequested integrationEvent, CancellationToken cancellationToken) => applier.ApplyAsync(cancellationToken);
}
