using Frappe.Notifications.Application.Messages;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>A marketing kind of the tests, the only category a recipient can turn off.</summary>
internal sealed class TestMarketingNotification : INotification<TestPayload>
{
    public static string Kind => "test_marketing";

    public static NotificationCategory Category => NotificationCategory.Marketing;

    public static TestPayload Sample => new("ana@example.com", null);

    public Recipient RecipientOf(TestPayload payload) => Recipients.Of(TestChannels.Mail, payload);
}
