using System.Text.Json;
using Frappe.Aggregates;

namespace Frappe.Notifications.Domain.Requests;

/// <summary>
/// An explicit request to send one notification was accepted (S2): <paramref name="RequestId"/> identifies it across HTTP retries, and the
/// payload travels only inside the outbox row and the stream, never a business table (S13). Infrastructure translates it into the
/// integration event.
/// </summary>
/// <param name="RequestId">The request's identity; the pipeline's source message id.</param>
/// <param name="Kind">The kind to send, already known to the catalog.</param>
/// <param name="Payload">The kind's payload as the caller sent it, already checked against the kind.</param>
public sealed record NotificationRequestAccepted(Guid RequestId, string Kind, JsonElement Payload) : IDomainEvent
{
    public override string ToString() => $"NotificationRequestAccepted {{ RequestId = {RequestId}, Kind = {Kind}, Payload = *** }}";
}
