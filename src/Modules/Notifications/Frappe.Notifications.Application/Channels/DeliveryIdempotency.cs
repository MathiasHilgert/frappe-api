using System.Security.Cryptography;
using System.Text;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Channels;

/// <summary>
/// The provider idempotency key (S14): derived only from the message id, the kind, the recipient key and the channel, so a redelivery
/// of the same message sends the same key and the provider sends once. It is a hash, so the key reveals no recipient.
/// </summary>
public static class DeliveryIdempotency
{
    public static string Key(Guid sourceMessageId, string kind, RecipientKey recipientKey, ChannelName channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(recipientKey);
        ArgumentNullException.ThrowIfNull(channel);
        var text = $"{sourceMessageId:D}\n{kind}\n{recipientKey.Value}\n{channel.Value}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
