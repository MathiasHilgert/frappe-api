namespace Frappe.Notifications.Senders.Email;

/// <summary>Which adapter takes rendered emails to their recipients (<c>Notifications:Email:Sender</c>).</summary>
public enum EmailSenderKind
{
    /// <summary>Resend, over HTTPS; needs <c>Notifications:Email:Resend:ApiKey</c>. The production value.</summary>
    Resend,

    /// <summary>Nothing leaves the process: each send is logged without its content. For development and tests.</summary>
    Logging,
}
