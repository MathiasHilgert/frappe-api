using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.Messages.Account;

/// <summary>
/// The Security email to the real owner of an address when somebody else tried to register it (S18): a heading, one intro and a footer. It has
/// no code and no link: the caller of the registration got the same answer as for a new address, so the owner is the only one who learns of it.
/// </summary>
public sealed class RegistrationAttempted : IEmailNotification<RegistrationAttemptedPayload>
{
    public static string Kind => "registration_attempted";

    public static NotificationCategory Category => NotificationCategory.Security;

    public static RegistrationAttemptedPayload Sample => new("ana@example.com", "es-AR");

    public Recipient RecipientOf(RegistrationAttemptedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Recipient.Contact(EmailChannel.Channel, payload.Email, NotificationLanguages.Parse(payload.Language));
    }

    public EmailDraft ToEmail(RegistrationAttemptedPayload payload, NotificationTexts texts)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(texts);
        return Email.Secure(texts["subject"])
            .Preheader(texts["preheader"])
            .Heading(texts["heading"])
            .Paragraph(texts["intro"])
            .Footer(texts["footer"]);
    }
}
