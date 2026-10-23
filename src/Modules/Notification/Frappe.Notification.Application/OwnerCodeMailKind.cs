namespace Frappe.Notification.Application;

/// <summary>The three mails that carry identity's one-time codes.</summary>
public enum OwnerCodeMailKind
{
    Verification,
    PasswordReset,
    EmailChange,
}
