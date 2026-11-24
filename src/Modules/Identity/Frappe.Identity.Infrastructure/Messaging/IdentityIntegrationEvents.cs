using Frappe.Aggregates;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Sessions;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Persistence;
using Published = Frappe.Identity.Contracts.Sessions;
using PublishedOwners = Frappe.Identity.Contracts.Owners;

namespace Frappe.Identity.Infrastructure.Messaging;

/// <summary>
/// Turns the module's domain events into integration events (EVT-03). The facts of the catalog (<c>Frappe.Identity.Contracts</c>) are published
/// as the aggregates that raise them arrive, each with its arm here; those of sessions and of owners are translated so far.
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
        OwnerRegistered registered => [new PublishedOwners.OwnerRegistered(registered.OwnerId, registered.At.ToDateTimeOffset())],
        OwnerRegistrationAttempted attempted => [new PublishedOwners.OwnerRegistrationAttempted(
            attempted.Email, attempted.Language, attempted.At.ToDateTimeOffset())],
        OwnerCodeIssued issued => [new PublishedOwners.OwnerCodeIssued(
            issued.OwnerId, StoredEnum<CodePurpose>.ToText(issued.Purpose), issued.Email, issued.Name, issued.Language, issued.Code,
            issued.At.ToDateTimeOffset())],
        OwnerVerified verified => [new PublishedOwners.OwnerVerified(verified.OwnerId, verified.At.ToDateTimeOffset())],
        _ => [],
    };
}
