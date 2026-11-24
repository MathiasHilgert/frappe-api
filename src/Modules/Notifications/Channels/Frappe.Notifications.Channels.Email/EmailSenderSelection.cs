namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Chooses the email adapter once, at composition, from <c>Notifications:Email:Sender</c> (DES-04): use cases never branch on the
/// environment. The options validator has already refused a Resend selection with no key, so a wrong deploy fails at start.
/// </summary>
internal static class EmailSenderSelection
{
    public static IEmailSender Select(EmailSenderKind kind, IEmailSender resend, IEmailSender logging) => kind switch
    {
        EmailSenderKind.Resend => resend,
        EmailSenderKind.Logging => logging,
        _ => throw new InvalidOperationException($"Configuration '{EmailOptions.Section}:Sender' names no email sender ({kind})."),
    };
}
