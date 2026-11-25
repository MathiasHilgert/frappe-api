using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.Messages.Account;

/// <summary>
/// The Security email with the code of the <c>email_change_code</c> flow (S10): a heading, one intro, the code in boxes, how long it lives
/// and a footer.
/// </summary>
public sealed class EmailChangeCode : IEmailNotification<EmailChangeCodePayload>
{
    public static string Kind => "email_change_code";

    public static NotificationCategory Category => NotificationCategory.Security;

    public static EmailChangeCodePayload Sample => new("ana@example.com", "es-AR", "Ana", "482913");

    public Recipient RecipientOf(EmailChangeCodePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Recipient.Contact(EmailChannel.Channel, payload.Email, NotificationLanguages.Parse(payload.Language));
    }

    public EmailDraft ToEmail(EmailChangeCodePayload payload, NotificationTexts texts)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(texts);
        return Email.Secure(texts["subject", new { payload.Code }])
            .Preheader(texts["preheader"])
            .Heading(texts["heading"])
            .Paragraph(texts["intro", new { payload.Name }])
            .Code(payload.Code)
            .Meta(texts["expires"], texts["single_use"])
            .Footer(texts["footer"]);
    }
}
