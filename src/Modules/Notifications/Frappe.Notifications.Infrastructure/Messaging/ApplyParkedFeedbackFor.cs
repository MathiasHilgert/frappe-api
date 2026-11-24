using Frappe.IntegrationEvents;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// The same step for the consumer of a producer's fact (S2a): whichever event carried the request, the staged deliveries are the same.
/// </summary>
internal sealed class ApplyParkedFeedbackFor<TEvent>(ParkedFeedbackApplier applier) : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken) => applier.ApplyAsync(cancellationToken);
}
