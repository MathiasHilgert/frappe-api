using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.UnitTests.Dispatching;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>A kind whose text needs an argument the draft does not pass.</summary>
internal sealed class MissingArgumentNotification : IEmailNotification<TestPayload>
{
    public static string Kind => "missing_argument";

    public static NotificationCategory Category => NotificationCategory.Transactional;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipient.Contact(EmailChannel.Channel, payload.Address!);

    public EmailDraft ToEmail(TestPayload payload, NotificationTexts texts) => Email.Standard(texts["subject", new { Other = 1 }]);
}
