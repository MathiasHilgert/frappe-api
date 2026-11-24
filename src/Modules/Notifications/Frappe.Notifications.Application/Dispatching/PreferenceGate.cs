using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>Pipeline step 2: lets a notification through unless the recipient turned its category off; only marketing can be (S9).</summary>
public sealed class PreferenceGate(IPreferenceStore preferences)
{
    public async Task<bool> AllowsAsync(RecipientKey key, NotificationCategory category, CancellationToken cancellationToken) =>
        !category.CanOptOut || !await preferences.IsOptedOutAsync(key, category, cancellationToken);
}
