using System.Text.Json;
using Frappe.IntegrationEvents;

namespace Frappe.Notifications.Contracts;

/// <summary>
/// A request to send one notification was accepted, published on the module's own stream for its dispatch consumer (S2). Nobody else
/// consumes it: it is how the explicit path reaches the Worker, not a fact other modules may build on. The payload is the kind's own,
/// versioned per kind in the catalog, and holds personal data: <see cref="ToString"/> prints none (S13, EVT-20).
/// </summary>
/// <param name="RequestId">Identifies the request across HTTP retries; the pipeline's source message id, so redelivery sends once.</param>
/// <param name="Kind">The kind to send, lower snake case.</param>
/// <param name="Payload">The kind's payload as JSON.</param>
[IntegrationEvent("frappe.notifications.notification_requested.v1")]
public sealed record NotificationRequested(Guid RequestId, string Kind, JsonElement Payload) : IIntegrationEvent
{
    public override string ToString() => $"NotificationRequested {{ RequestId = {RequestId}, Kind = {Kind}, Payload = *** }}";
}
