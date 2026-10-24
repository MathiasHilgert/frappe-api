namespace Frappe.Notification.Application.OwnerCodeMails;

/// <summary>The three mails that carry identity's one-time codes.</summary>
public enum OwnerCodeMailKind
{
    Verification,
    PasswordReset,
    EmailChange,
}
