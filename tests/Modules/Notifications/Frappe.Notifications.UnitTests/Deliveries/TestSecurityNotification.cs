using Frappe.Notifications.Application.Messages;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>A security kind of the tests; an address payload goes to an explicit contact, a reference payload to the directory.</summary>
internal sealed class TestSecurityNotification : INotification<TestPayload>
{
    public static string Kind => "test_security";

    public static NotificationCategory Category => NotificationCategory.Security;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipients.Of(TestChannels.Mail, payload);
}
