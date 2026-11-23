using Frappe.Messaging.Inbox;
using Frappe.Messaging.Relay;
using NATS.Client.Core;
using NodaTime.Text;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// CloudEvents 1.0 in binary content mode over NATS headers: every attribute as a <c>ce-</c> header, the payload as
/// the message body and <c>datacontenttype</c> as <c>content-type</c>, the same mapping the CloudEvents Go SDK uses
/// for NATS (the published v1.0.1 NATS binding predates NATS headers and defines only structured mode).
/// W3C trace context travels as <c>traceparent</c> / <c>tracestate</c>, as in the distributed tracing extension.
/// </summary>
public static class CloudEventHeaders
{
    public const string SpecVersion = "ce-specversion";
    public const string Id = "ce-id";
    public const string Type = "ce-type";
    public const string Source = "ce-source";
    public const string Time = "ce-time";
    public const string ContentType = "content-type";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";

    public static NatsHeaders From(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var headers = new NatsHeaders
        {
            [SpecVersion] = "1.0",
            [Id] = message.Id.ToString(),
            [Type] = message.Type,
            [Source] = message.Source,
            [Time] = InstantPattern.ExtendedIso.Format(message.Time),
            [ContentType] = message.DataContentType,
        };
        if (message.TraceParent is not null)
        {
            headers[TraceParent] = message.TraceParent;
        }

        if (message.TraceState is not null)
        {
            headers[TraceState] = message.TraceState;
        }

        return headers;
    }

    /// <summary>Reads a received message; a message without a valid <c>ce-id</c> and <c>ce-type</c> is malformed.</summary>
    public static IncomingMessage Read(NatsHeaders? headers, ReadOnlyMemory<byte> data)
    {
        var id = Single(headers, Id);
        var type = Single(headers, Type);
        if (!Guid.TryParse(id, out var messageId) || string.IsNullOrWhiteSpace(type))
        {
            throw new FormatException($"Message is not a Frappe CloudEvent (ce-id '{id}', ce-type '{type}').");
        }

        return new IncomingMessage(messageId, type, data, Single(headers, TraceParent), Single(headers, TraceState));
    }

    private static string? Single(NatsHeaders? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var values) ? values.ToString() : null;
}
