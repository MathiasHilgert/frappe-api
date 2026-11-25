using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.UnitTests.Dispatching;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>A Security kind that sneaks a link in through <see cref="Email.Standard"/>: the startup check must refuse it.</summary>
internal sealed class LinkedSecurityNotification : IEmailNotification<TestPayload>
{
    public static string Kind => "linked_security";

    public static NotificationCategory Category => NotificationCategory.Security;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipient.Contact(EmailChannel.Channel, payload.Address!);

    public EmailDraft ToEmail(TestPayload payload, NotificationTexts texts) =>
        Email.Standard(texts["subject"]).Heading(texts["subject"]).Action("Go", "https://frappe.example/x");
}
