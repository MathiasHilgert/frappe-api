using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Channels;

/// <summary>
/// One way to reach a recipient (email, SMS, push), contributed by a plug-in and resolved as an <c>IEnumerable</c>. It renders and
/// sends; the pipeline decides whether to (Strategy per channel). Adding a channel adds an adapter and changes no core type (S0).
/// </summary>
public interface INotificationChannel
{
    ChannelName Name { get; }

    /// <summary>Whether the kind can be delivered here: it implements the channel's content interface.</summary>
    bool CanDeliver(NotificationKind kind);

    /// <summary>Renders and sends. A transient provider fault throws, so the message is redelivered (S14).</summary>
    Task<ChannelOutcome> DeliverAsync(DispatchItem item, CancellationToken cancellationToken);
}
