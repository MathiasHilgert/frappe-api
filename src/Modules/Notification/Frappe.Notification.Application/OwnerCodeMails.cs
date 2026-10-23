using Frappe.Notification.Domain;

namespace Frappe.Notification.Application;

/// <summary>The plain-text mails that carry identity's one-time codes.</summary>
public static class OwnerCodeMails
{
    private const string Expiry = "It expires in 15 minutes. If you did not ask for it, ignore this email.";

    public static Mail Verification(string to, string code) =>
        new(to, $"{code} is your Frappé verification code", $"Your verification code is {code}.\n\n{Expiry}");

    public static Mail PasswordReset(string to, string code) =>
        new(to, $"{code} is your Frappé password reset code", $"Use {code} to reset your password.\n\n{Expiry}");

    public static Mail EmailChange(string to, string code) =>
        new(to, $"{code} confirms your new Frappé email", $"Use {code} to confirm this is your new email.\n\n{Expiry}");
}
