using Frappe.Aggregates;
using Frappe.IntegrationEvents;

namespace Frappe.Messaging.Outbox;

/// <summary>Turns a module's domain events into the integration events other modules may see.</summary>
public interface IIntegrationEventTranslator
{
    /// <summary>The integration events a domain event produces; none when the event is internal to the module.</summary>
    IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent);
}
