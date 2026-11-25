using Frappe.Notifications.Application.Messages;

namespace Frappe.Notifications.Application.Deliveries;

/// <summary>One notification to deliver: the message that asked, its kind and its payload. The payload never prints (S13).</summary>
/// <param name="SourceMessageId">The id of the message that asked; it makes redelivery idempotent (S14).</param>
/// <param name="Kind">The kind.</param>
/// <param name="Payload">The payload, of the kind's payload type.</param>
public sealed record DeliveryRequest(Guid SourceMessageId, NotificationKind Kind, object Payload)
{
    public override string ToString() => $"DeliveryRequest {{ SourceMessageId = {SourceMessageId}, Kind = {Kind.Name}, Payload = *** }}";
}
