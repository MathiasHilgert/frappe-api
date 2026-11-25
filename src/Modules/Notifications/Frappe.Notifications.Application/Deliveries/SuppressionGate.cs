using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Application.Suppressions;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Deliveries;

/// <summary>Pipeline step 4: stops a delivery to an address suppressed over the channel, probing every active hash key.</summary>
public sealed class SuppressionGate(ISuppressionStore suppressions, IAddressHasher hasher)
{
    public Task<bool> IsSuppressedAsync(ChannelName channel, ContactRecipient contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return suppressions.IsSuppressedAsync(channel, hasher.HashWithEveryKey(contact.Address), cancellationToken);
    }
}
