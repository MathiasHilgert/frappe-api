using System.Diagnostics;
using Frappe.Messaging.Relay;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// Publishes to JetStream and returns only after the stream stored the message. It tells the relay apart a message
/// the broker will never take (<see cref="MessageRejectedException"/>) from a broker that cannot take anything right
/// now (any other exception): only the first may count against the message.
/// The subject is the message's CloudEvents type under <paramref name="naming"/> (<see cref="JetStreamNaming.Default"/>
/// when null); the <c>ce-type</c> header is always the plain type.
/// </summary>
/// <param name="js">The JetStream context to publish through.</param>
/// <param name="naming">The stream and subject names; null means the production names.</param>
public sealed class JetStreamPublisher(INatsJSContext js, JetStreamNaming? naming = null) : IMessagePublisher
{
    private readonly JetStreamNaming _naming = naming ?? JetStreamNaming.Default;

    /// <summary>
    /// JetStream API errors caused by the message itself. Everything else the API can answer while publishing
    /// (stream full, insufficient resources, temporarily unavailable, no leader) is about the stream, not the message.
    /// </summary>
    private static readonly HashSet<int> MessageErrorCodes =
    [
        10054, // JSStreamMessageExceedsMaximumErr: message size exceeds the stream's max_msg_size
    ];

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // NATS.Net writes its own publish span into the traceparent header. Making the message's context the
        // current one keeps that span in the original trace instead of letting it start a new, disconnected one.
        using var context = ContinueTrace(message);
        try
        {
            var ack = await js.PublishAsync(
                _naming.Subject(message.Type),
                message.Data.ToArray(),
                opts: new NatsJSPubOpts { MsgId = message.Id.ToString() },
                headers: CloudEventHeaders.From(message),
                cancellationToken: cancellationToken);
            ack.EnsureSuccess();
        }
        catch (NatsJSDuplicateMessageException)
        {
            // An earlier attempt already stored it (the relay retried after a lost ack). Nothing to do.
        }
        catch (NatsJSApiException refused) when (MessageErrorCodes.Contains(refused.Error.ErrCode))
        {
            throw new MessageRejectedException($"JetStream rejected the message ({refused.Error.ErrCode}): {refused.Message}", refused);
        }
        catch (NatsPayloadTooLargeException tooLarge)
        {
            // Checked by the client before sending: this payload exceeds the server's max_payload and never will fit.
            throw new MessageRejectedException($"The message exceeds the server's maximum payload: {tooLarge.Message}", tooLarge);
        }
    }

    private static Activity? ContinueTrace(OutgoingMessage message)
    {
        if (message.TraceParent is null || Activity.Current?.Id == message.TraceParent)
        {
            return null;
        }

        var context = new Activity("frappe.messaging.trace-context");
        context.SetParentId(message.TraceParent);
        context.TraceStateString = message.TraceState;
        return context.Start();
    }
}
