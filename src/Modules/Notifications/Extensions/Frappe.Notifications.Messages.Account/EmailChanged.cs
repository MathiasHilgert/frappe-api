using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.Messages.Account;

/// <summary>
/// The email to the old address when a change of the account email was confirmed (S15): it says the address changed and offers a button that
/// restores it. It is Transactional, not Security: it carries the undo link, and a Security mail cannot carry one (S7). Neither can be turned off.
/// </summary>
public sealed class EmailChanged : IEmailNotification<EmailChangedPayload>
{
    public static string Kind => "email_changed";

    public static NotificationCategory Category => NotificationCategory.Transactional;

    public static EmailChangedPayload Sample => new("ana@example.com", "es-AR", "Ana", "https://console.example.com/undo-email-change?token=sample");

    public Recipient RecipientOf(EmailChangedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Recipient.Contact(EmailChannel.Channel, payload.Email, NotificationLanguages.Parse(payload.Language));
    }

    public EmailDraft ToEmail(EmailChangedPayload payload, NotificationTexts texts)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(texts);
        return Email.Standard(texts["subject"])
            .Preheader(texts["preheader"])
            .Heading(texts["heading"])
            .Paragraph(texts["intro", new { payload.Name }])
            .Action(texts["undo"], payload.UndoUrl)
            .Footer(texts["footer"]);
    }
}
