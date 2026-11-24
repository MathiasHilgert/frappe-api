using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.UnitTests.Dispatching;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>A kind whose draft cannot be built for its payload (a link that is not https): a composition error, never a transient one.</summary>
internal sealed class BadLinkNotification : IEmailNotification<TestPayload>
{
    public static string Kind => "bad_link";

    public static NotificationCategory Category => NotificationCategory.Transactional;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipient.Contact(EmailChannel.Channel, payload.Address!);

    public EmailDraft ToEmail(TestPayload payload, NotificationTexts texts) =>
        Email.Standard(texts["subject"]).Action("Go", "http://frappe.example/x");
}
