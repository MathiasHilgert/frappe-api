using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Dispatching;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>What the claim's step does, whatever event started it.</summary>
internal sealed class ParkedFeedbackApplier(NotificationsDbContext db, IDeliveryStaging staging, PendingDeliveries pending)
{
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // This step runs only for the winner of the claim, so its pending deliveries are the ones to count after the commit.
        pending.MarkClaimed();
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
