namespace Frappe.Notifications.Application.Triggers;

/// <summary>
/// The producer plug-in's mapper (S2a): it turns one integration event of another module into the notifications that fact asks for, one
/// per recipient and kind. It is pure: it reads the event and nothing else, so the plug-in knows the producer's event and the kinds, and the
/// producer knows nothing about Notifications. It is registered with <c>AddNotificationProducer</c> in Infrastructure.
/// </summary>
/// <typeparam name="TEvent">The integration event the producer's module publishes.</typeparam>
public interface INotificationProducer<in TEvent>
{
    /// <summary>The notifications <paramref name="integrationEvent"/> asks for; none when the fact needs no notification.</summary>
    IReadOnlyList<ProducedNotification> Map(TEvent integrationEvent);
}
