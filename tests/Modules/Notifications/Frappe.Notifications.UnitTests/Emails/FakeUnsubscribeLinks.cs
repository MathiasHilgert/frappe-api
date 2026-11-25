using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>Deterministic unsubscribe links, as the preferences slice will make them with a signed token.</summary>
internal sealed class FakeUnsubscribeLinks : IEmailUnsubscribeLinks
{
    public UnsubscribeLink LinkFor(RecipientKey recipient, NotificationCategory category) =>
        new(
            new Uri("https://frappe.example/unsubscribe?c=marketing"),
            new Uri("https://api.frappe.example/v1/notifications/unsubscriptions?token=t0k3n"));
}
