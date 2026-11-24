using Frappe.Aggregates;
using Frappe.Identity.Domain.Sessions;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Persistence;
using Published = Frappe.Identity.Contracts.Sessions;

namespace Frappe.Identity.Infrastructure.Messaging;

/// <summary>
/// Turns the module's domain events into integration events (EVT-03). The facts of the catalog (<c>Frappe.Identity.Contracts</c>) are published
/// as the aggregates that raise them arrive, each with its arm here; those of sessions are translated so far.
/// </summary>
internal sealed class IdentityIntegrationEvents : IIntegrationEventTranslator
{
    public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        SessionOpened opened => [new Published.SessionOpened(
            opened.PrincipalId, opened.SessionId, StoredEnum<SessionAudience>.ToText(opened.Audience), opened.At.ToDateTimeOffset())],
        SessionClosed closed => [new Published.SessionClosed(closed.PrincipalId, closed.SessionId, closed.At.ToDateTimeOffset())],
        SessionRevoked revoked => [new Published.SessionRevoked(
            revoked.PrincipalId, revoked.SessionId, StoredEnum<RevocationReason>.ToText(revoked.Reason), revoked.At.ToDateTimeOffset())],
        _ => [],
    };
}
