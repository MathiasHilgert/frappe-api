using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.Messages.Marketing;

/// <summary>
/// A Marketing email announcing news (S10): the headline, the summary and one button. Marketing can be turned off, so the email channel adds
/// its footer with the unsubscribe link and headers: this kind never writes them (S9).
/// </summary>
public sealed class ProductAnnouncement : IEmailNotification<ProductAnnouncementPayload>
{
    public static string Kind => "product_announcement";

    public static NotificationCategory Category => NotificationCategory.Marketing;

    public static ProductAnnouncementPayload Sample => new(
        "ana@example.com", "es-AR", "Ana", "Frappé ya acepta pagos", "Ahora podés cobrar desde la app.", "https://frappe.example/news");

    public Recipient RecipientOf(ProductAnnouncementPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Recipient.Contact(EmailChannel.Channel, payload.Email, NotificationLanguages.Parse(payload.Language));
    }

    public EmailDraft ToEmail(ProductAnnouncementPayload payload, NotificationTexts texts)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(texts);
        return Email.Standard(texts["subject", new { payload.Headline }])
            .Preheader(texts["preheader"])
            .Heading(payload.Headline)
            .Paragraph(payload.Summary)
            .Action(texts["cta"], payload.Url);
    }
}
