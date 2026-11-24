using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Kinds;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// Pipeline step 3: pairs the kind's channels with the contacts that can be reached over them. A recipient gets one delivery per channel:
/// when a reference has several contacts on a channel, the first, which the resolver ranks as preferred, is used.
/// </summary>
public sealed class ChannelSelection(IEnumerable<INotificationChannel> channels)
{
    private readonly INotificationChannel[] _channels = [.. channels];

    /// <summary>Every channel that can carry the kind.</summary>
    public IReadOnlyList<INotificationChannel> ChannelsFor(NotificationKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return [.. _channels.Where(channel => channel.CanDeliver(kind))];
    }

    /// <summary>The channels that can carry the kind, each with the contact to reach the recipient over it.</summary>
    public IReadOnlyList<(INotificationChannel Channel, ResolvedContact Contact)> Select(
        NotificationKind kind, IReadOnlyList<ResolvedContact> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        return
        [
            .. ChannelsFor(kind)
                .Select(channel => (Channel: channel, Contact: contacts.FirstOrDefault(contact => contact.Contact.Channel == channel.Name)))
                .Where(pair => pair.Contact is not null)
                .Select(pair => (pair.Channel, pair.Contact!)),
        ];
    }
}
