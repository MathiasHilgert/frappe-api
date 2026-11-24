using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;

namespace Frappe.Notifications.Application.Ports;

/// <summary>What recipients turned off: read by the gate, added by the unsubscribe (it only stages, UOW-01).</summary>
public interface IPreferenceStore
{
    /// <summary>Whether the category is off for the recipient under any of its keys: an address has one per active hash key (N2-D2).</summary>
    Task<bool> IsOptedOutAsync(IReadOnlyList<RecipientKey> recipientKeys, NotificationCategory category, CancellationToken cancellationToken);

    /// <summary>Stages a preference; the unit of work saves it.</summary>
    void Stage(Preference preference);
}
