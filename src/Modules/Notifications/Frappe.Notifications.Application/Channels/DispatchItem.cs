using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Channels;

/// <summary>
/// One delivery handed to a channel: the kind and its payload to render, the contact to reach and the key the provider dedupes by. The
/// payload and the address never print (S13).
/// </summary>
/// <param name="SourceMessageId">The id of the message that asked.</param>
/// <param name="Kind">The kind, whose notification the channel renders from.</param>
/// <param name="Payload">The payload, of the kind's payload type.</param>
/// <param name="Contact">Where to reach the recipient.</param>
/// <param name="RecipientKey">The recipient's key.</param>
/// <param name="IdempotencyKey">The provider idempotency key (<see cref="DeliveryIdempotency"/>): the same for every attempt.</param>
public sealed record DispatchItem(
    Guid SourceMessageId, NotificationKind Kind, object Payload, ContactRecipient Contact, RecipientKey RecipientKey, string IdempotencyKey)
{
    public override string ToString() =>
        $"DispatchItem {{ SourceMessageId = {SourceMessageId}, Kind = {Kind.Name}, Channel = {Contact.Channel.Value}, Payload = *** }}";
}
