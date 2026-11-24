using System.Text.Json;
using Frappe.Aggregates;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Domain.Requests;

/// <summary>
/// The fact that an explicit request to send a notification was accepted (S2). It records only its identity and kind, never the payload
/// (S13): its reason to exist is to raise <see cref="NotificationRequestAccepted"/> in the same transaction as its row, which is how the
/// event reaches the outbox (EVT-04), and to make a retried request with the same identity collapse on its key.
/// </summary>
public sealed class SendRequest : AggregateRoot
{
    private SendRequest() => Kind = null!;

    private SendRequest(Guid id, string kind, Instant now)
    {
        Id = id;
        Kind = kind;
        RequestedAt = now;
    }

    /// <summary>The request's identity: derived from the caller's <c>Idempotency-Key</c>, or new when it sent none.</summary>
    public Guid Id { get; private set; }

    /// <summary>The kind requested, lower snake case.</summary>
    public string Kind { get; private set; }

    public Instant RequestedAt { get; private set; }

    /// <summary>Accepts the request and raises the event that carries <paramref name="payload"/> to the dispatch.</summary>
    public static SendRequest Accept(Guid requestId, string kind, JsonElement payload, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var request = new SendRequest(requestId, kind, now);
        request.Raise(new NotificationRequestAccepted(requestId, kind, payload));
        return request;
    }
}
