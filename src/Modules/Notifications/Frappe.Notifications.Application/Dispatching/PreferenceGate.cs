using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// Pipeline step 2: lets a notification through unless the recipient turned its category off; only marketing can be (S9). A recipient known
/// by an address is probed under every active hash key, so an opt-out made before a key rotation still holds after it (N2-D2).
/// </summary>
public sealed class PreferenceGate(IPreferenceStore preferences, IAddressHasher hasher)
{
    public async Task<bool> AllowsAsync(ResolvedContact contact, NotificationCategory category, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return !category.CanOptOut || !await preferences.IsOptedOutAsync(KeysOf(contact), category, cancellationToken);
    }

    private IReadOnlyList<RecipientKey> KeysOf(ResolvedContact contact) =>
        contact.Key.IsAddress
            ? [.. hasher.HashWithEveryKey(contact.Contact.Address).Select(RecipientKey.Of)]
            : [contact.Key];
}
