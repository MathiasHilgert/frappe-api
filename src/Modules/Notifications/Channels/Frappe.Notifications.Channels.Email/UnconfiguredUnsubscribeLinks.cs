using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Channels.Email;

/// <summary>The links port with no implementation behind it: it fails loudly, so no Marketing mail leaves without its link.</summary>
internal sealed class UnconfiguredUnsubscribeLinks : IEmailUnsubscribeLinks
{
    public UnsubscribeLink LinkFor(RecipientKey recipient, NotificationCategory category) =>
        throw new InvalidOperationException(
            "No IEmailUnsubscribeLinks is registered: a Marketing email cannot be rendered without its unsubscribe link.");
}
