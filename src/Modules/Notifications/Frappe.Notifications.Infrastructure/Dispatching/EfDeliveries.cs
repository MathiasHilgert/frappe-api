using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>Finds a delivery by the provider's message id through its unique index; the row stays tracked for the unit of work.</summary>
internal sealed class EfDeliveries(NotificationsDbContext db) : IDeliveries
{
    public Task<Delivery?> FindByProviderMessageAsync(DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken) =>
        db.Deliveries.FirstOrDefaultAsync(d => d.Provider == provider && d.ProviderMessageId == providerMessageId, cancellationToken);
}
