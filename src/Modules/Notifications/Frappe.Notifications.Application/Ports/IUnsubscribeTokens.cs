using Frappe.Notifications.Domain.Deliveries;
using Frappe.Results;

namespace Frappe.Notifications.Application.Ports;

/// <summary>
/// Signs and reads the opaque token an unsubscribe link carries (S5): a recipient key and the category to turn off. A token never expires, so
/// a link in an old mail still works, and it is deterministic, so a mail renders the same on every attempt (S14).
/// </summary>
public interface IUnsubscribeTokens
{
    /// <summary>The token for <paramref name="recipient"/> and <paramref name="category"/>.</summary>
    /// <exception cref="ArgumentException">The category cannot be turned off: no link is ever made for it.</exception>
    string Issue(RecipientKey recipient, NotificationCategory category);

    /// <summary>
    /// What a token says, or <see cref="UnsubscribeToken.InvalidCode"/> for any text that is not one of ours: malformed, tampered, signed with
    /// a key no longer known, or for a category that cannot be turned off. It never says which.
    /// </summary>
    Result<UnsubscribeClaim> Verify(string? token);
}
