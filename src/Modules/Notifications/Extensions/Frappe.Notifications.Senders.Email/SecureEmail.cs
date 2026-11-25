namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The content of a Security email (a code, an alert). It has no <c>Action</c> method: a link in a Security mail does not compile (S7).
/// </summary>
public sealed class SecureEmail : EmailDraft<SecureEmail>
{
    internal SecureEmail(string subject)
        : base(subject)
    {
    }
}
