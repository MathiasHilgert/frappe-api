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
        return db.Deliveries.AsNoTracking().AnyAsync(
            d => d.SourceMessageId == target.SourceMessageId && d.Kind == target.Kind && d.RecipientKey == key && d.Channel == channel,
            cancellationToken);
    }

    public void Stage(Delivery delivery) => db.Deliveries.Add(delivery);
}
