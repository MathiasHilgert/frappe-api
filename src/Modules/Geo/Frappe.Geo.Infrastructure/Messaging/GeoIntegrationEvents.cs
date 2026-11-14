using Frappe.Aggregates;
using Frappe.Geo.Contracts.Holidays;
using Frappe.Geo.Domain.Holidays;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Outbox;

namespace Frappe.Geo.Infrastructure.Messaging;

/// <summary>Which Geo domain events other modules get to know about, and in what shape.</summary>
internal sealed class GeoIntegrationEvents : IIntegrationEventTranslator
{
    public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        HolidayCalendarChanged changed => [new HolidaysChanged(changed.Jurisdiction.Value, changed.Year.Value) { Scope = changed.Scope.Value }],
        _ => [],
    };
}
