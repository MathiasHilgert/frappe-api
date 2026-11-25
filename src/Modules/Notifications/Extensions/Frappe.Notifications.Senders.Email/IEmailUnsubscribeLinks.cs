using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// Makes the unsubscribe links of a Marketing mail (S5, S9): the one-click URL and the page of the frontend, both carrying the signed token.
/// <see cref="SignedUnsubscribeLinks"/> is the implementation a host registers.
/// </summary>
public interface IEmailUnsubscribeLinks
{
    /// <summary>The links for <paramref name="recipient"/> and <paramref name="category"/>.</summary>
    UnsubscribeLink LinkFor(RecipientKey recipient, NotificationCategory category);
}
