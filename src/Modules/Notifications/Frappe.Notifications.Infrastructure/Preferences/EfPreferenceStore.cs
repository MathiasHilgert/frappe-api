using Frappe.Notifications.Application.Preferences;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Preferences;

/// <summary>Reads preferences by recipient key and category, and stages the ones an unsubscribe makes.</summary>
internal sealed class EfPreferenceStore(NotificationsDbContext db) : IPreferenceStore
{
    public Task<bool> IsOptedOutAsync(
        IReadOnlyList<RecipientKey> recipientKeys, NotificationCategory category, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipientKeys);
        var keys = recipientKeys.Select(key => key.Value).ToArray();
        return db.Preferences.AsNoTracking().AnyAsync(p => keys.Contains(p.RecipientKey) && p.Category == category, cancellationToken);
    }

    public void Stage(Preference preference) => db.Preferences.Add(preference);
}
