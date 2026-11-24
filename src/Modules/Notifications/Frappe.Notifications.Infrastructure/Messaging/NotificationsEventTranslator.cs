using Frappe.Aggregates;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Notifications.Contracts;
using Frappe.Notifications.Domain.Requests;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>Turns the module's domain events into integration events: an accepted send request becomes the event its dispatch consumes.</summary>
internal sealed class NotificationsEventTranslator : IIntegrationEventTranslator
{
    public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        NotificationRequestAccepted accepted => [new NotificationRequested(accepted.RequestId, accepted.Kind, accepted.Payload)],
        _ => [],
    };
}
