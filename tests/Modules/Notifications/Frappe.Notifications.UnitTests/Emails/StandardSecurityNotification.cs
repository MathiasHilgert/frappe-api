using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.UnitTests.Dispatching;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>A Security kind that returns a Standard email, even one with no link: a Security kind returns <see cref="SecureEmail"/>.</summary>
internal sealed class StandardSecurityNotification : IEmailNotification<TestPayload>
{
    public static string Kind => "standard_security";

    public static NotificationCategory Category => NotificationCategory.Security;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipient.Contact(EmailChannel.Channel, payload.Address!);

    public EmailDraft ToEmail(TestPayload payload, NotificationTexts texts) => Email.Standard(texts["subject"]).Heading(texts["subject"]);
}
