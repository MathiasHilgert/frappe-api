using Frappe.Aggregates;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Storage.Outbox;

namespace Frappe.Identity.Infrastructure.Messaging;

/// <summary>
/// Turns the module's domain events into integration events (EVT-03). The facts of the catalog (<c>Frappe.Identity.Contracts</c>) are published
/// as the aggregates that raise them arrive, each with its arm here; none is raised yet, so nothing is translated.
/// </summary>
internal sealed class IdentityIntegrationEvents : IIntegrationEventTranslator
{
    public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        _ => [],
    };
}
