using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.ProviderEvents;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>Reads and stages the provider event records on the module's context; the unit of work saves them (UOW-01).</summary>
internal sealed class EfProviderEvents(NotificationsDbContext db) : IProviderEvents
{
    public Task<bool> ExistsAsync(DeliveryProvider provider, string eventId, CancellationToken cancellationToken) =>
        db.ProviderEvents.AsNoTracking().AnyAsync(e => e.Provider == provider && e.EventId == eventId, cancellationToken);

    public void Stage(ProviderEvent providerEvent) => db.ProviderEvents.Add(providerEvent);
}
