using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Makes the unsubscribe links of a Marketing mail (S5, S9). The signed token and the page URL arrive with the preferences slice, which
/// registers the real implementation; until then the channel refuses to render a Marketing mail rather than send one with no way out.
/// </summary>
public interface IEmailUnsubscribeLinks
{
    /// <summary>The links for <paramref name="recipient"/> and <paramref name="category"/>.</summary>
    UnsubscribeLink LinkFor(RecipientKey recipient, NotificationCategory category);
}
