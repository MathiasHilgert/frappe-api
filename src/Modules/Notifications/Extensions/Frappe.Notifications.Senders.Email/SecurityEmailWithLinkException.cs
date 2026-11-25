namespace Frappe.Notifications.Senders.Email;

/// <summary>A Security kind returned a draft that is not a <see cref="SecureEmail"/>: a bug of the kind that no retry fixes.</summary>
/// <param name="message">What the kind must return instead.</param>
internal sealed class SecurityEmailWithLinkException(string message) : ArgumentException(message);
