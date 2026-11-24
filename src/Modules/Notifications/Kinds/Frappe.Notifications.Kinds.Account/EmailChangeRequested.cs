using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Kinds.Account;

/// <summary>
/// The Security email to the old address when a change of the account email was requested (S18): a heading, one intro that names the requested
/// address with its local part masked, and a footer that says what to do if it was not the owner. It has no code and no link.
/// </summary>
public sealed class EmailChangeRequested : IEmailNotification<EmailChangeRequestedPayload>
{
    public static string Kind => "email_change_requested";

    public static NotificationCategory Category => NotificationCategory.Security;

    public static EmailChangeRequestedPayload Sample => new("ana@example.com", "es-AR", "Ana", "n***@example.com");

    public Recipient RecipientOf(EmailChangeRequestedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Recipient.Contact(EmailChannel.Channel, payload.Email, NotificationLanguages.Parse(payload.Language));
    }

    public EmailDraft ToEmail(EmailChangeRequestedPayload payload, NotificationTexts texts)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(texts);
        return Email.Secure(texts["subject"])
            .Preheader(texts["preheader"])
            .Heading(texts["heading"])
            .Paragraph(texts["intro", new { payload.Name, payload.NewEmailMasked }])
            .Footer(texts["footer"]);
    }
}
