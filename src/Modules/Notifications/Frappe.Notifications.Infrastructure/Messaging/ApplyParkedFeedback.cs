using Frappe.IntegrationEvents;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Contracts;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Dispatching;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// The dispatch consumer's step inside the claim transaction (Flow 3): for each delivery the before-claim step staged, it takes the lock of
/// the provider message (the webhook holds the same one around its lookup and parking) and applies what the provider reported meanwhile. The
/// lock lasts until the claim commits, so a webhook that finds no delivery has parked before this reads, and one that comes later waits and
/// finds the delivery. The claim saves the result with the deliveries.
/// </summary>
internal sealed class ApplyParkedFeedback(NotificationsDbContext db, IDeliveryStaging staging) : IIntegrationEventHandler<NotificationRequested>
{
    public async Task HandleAsync(NotificationRequested integrationEvent, CancellationToken cancellationToken)
    {
        var staged = db.ChangeTracker.Entries<Delivery>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.Provider is not null && entry.Entity.ProviderMessageId is not null)
            .Select(entry => entry.Entity)
            .OrderBy(delivery => delivery.ProviderMessageId, StringComparer.Ordinal)
            .ToList();
        foreach (var delivery in staged)
        {
            var provider = delivery.Provider!.Value;
            var messageId = delivery.ProviderMessageId!;
            var key = ProviderMessageLock.KeyOf(provider, messageId);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            foreach (var parked in await staging.ParkedForAsync(provider, messageId, cancellationToken))
            {
                _ = delivery.Apply(parked.Feedback, parked.OccurredAt);
            }
        }
    }
}
