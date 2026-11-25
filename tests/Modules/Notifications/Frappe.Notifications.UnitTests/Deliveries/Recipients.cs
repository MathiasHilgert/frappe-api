using Frappe.Localization;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>Reads the recipient out of a test payload.</summary>
internal static class Recipients
{
    public static Recipient Of(ChannelName channel, TestPayload payload) => payload.ReferenceId is { } id
        ? Recipient.Reference("account", id)
        : Recipient.Contact(channel, payload.Address!, LanguageTag.Create("es-AR").Value);
}
