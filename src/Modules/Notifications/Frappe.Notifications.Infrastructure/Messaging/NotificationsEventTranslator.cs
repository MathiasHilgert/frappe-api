using Frappe.Aggregates;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Storage.Outbox;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>Turns the module's domain events into integration events. It publishes none yet: its own stream arrives with the dispatch.</summary>
internal sealed class NotificationsEventTranslator : IIntegrationEventTranslator
{
    public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => [];
}
