namespace Frappe.Messaging.Relay;

/// <summary>
/// Outbound port of the relay: hands one message to the broker and returns only once it is stored there.
/// It throws <see cref="MessageRejectedException"/> when the broker refuses this particular message; any other
/// exception means the broker is unavailable and says nothing about the message.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}
