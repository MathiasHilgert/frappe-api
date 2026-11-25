using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.UnitTests.Deliveries;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>A kind whose draft reads a text its files do not have.</summary>
internal sealed class MissingKeyNotification : IEmailNotification<TestPayload>
{
    public static string Kind => "missing_key";

    public static NotificationCategory Category => NotificationCategory.Transactional;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipient.Contact(EmailChannel.Channel, payload.Address!);

    public EmailDraft ToEmail(TestPayload payload, NotificationTexts texts) => Email.Standard(texts["subject"]).Heading(texts["absent"]);
}
