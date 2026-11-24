using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>Reads preferences by recipient key and category.</summary>
internal sealed class EfPreferenceStore(NotificationsDbContext db) : IPreferenceStore
{
    public Task<bool> IsOptedOutAsync(RecipientKey recipientKey, NotificationCategory category, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipientKey);
        var key = recipientKey.Value;
        return db.Preferences.AsNoTracking().AnyAsync(p => p.RecipientKey == key && p.Category == category, cancellationToken);
    }
}
