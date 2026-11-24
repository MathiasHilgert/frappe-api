using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>Stages deliveries on the module's context and never saves: the claim's transaction does (ARCH-13, UOW-01).</summary>
internal sealed class EfDeliveryStaging(NotificationsDbContext db) : IDeliveryStaging
{
    public Task<bool> ExistsAsync(DeliveryTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var channel = target.Channel.Value;
        var key = target.RecipientKey.Value;

        // A second item of the same fact for the same target is staged but not saved yet: the database cannot see it.
        var staged = db.Deliveries.Local.Any(
            d => d.SourceMessageId == target.SourceMessageId && d.Kind == target.Kind && d.RecipientKey == key && d.Channel == channel);
        return staged ? Task.FromResult(true) : db.Deliveries.AsNoTracking().AnyAsync(
            d => d.SourceMessageId == target.SourceMessageId && d.Kind == target.Kind && d.RecipientKey == key && d.Channel == channel,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ParkedFeedback>> ParkedForAsync(
        DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken)
    {
        var parked = await db.ProviderEvents.AsNoTracking()
            .Where(e => e.Provider == provider && e.ProviderMessageId == providerMessageId && e.Feedback != null && e.OccurredAt != null)
            .Select(e => new { Feedback = e.Feedback!.Value, OccurredAt = e.OccurredAt!.Value })
            .ToListAsync(cancellationToken);
        return [.. parked.OrderBy(e => e.OccurredAt).Select(e => new ParkedFeedback(e.Feedback, e.OccurredAt))];
    }

    public void Stage(Delivery delivery) => db.Deliveries.Add(delivery);
}
