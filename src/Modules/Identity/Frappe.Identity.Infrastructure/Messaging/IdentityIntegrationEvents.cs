using Frappe.Aggregates;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Sessions;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Persistence;
using Published = Frappe.Identity.Contracts.Sessions;
using PublishedCredentials = Frappe.Identity.Contracts.Credentials;
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
        StepUpCompleted stepped => [new Published.StepUpCompleted(
            stepped.PrincipalId, stepped.SessionId, stepped.ValidUntil.ToDateTimeOffset(), stepped.At.ToDateTimeOffset())],
        PasswordChanged changed => [new PublishedCredentials.PasswordChanged(changed.PrincipalId, changed.At.ToDateTimeOffset())],
        PasswordReset reset => [new PublishedCredentials.PasswordReset(reset.PrincipalId, reset.At.ToDateTimeOffset())],
        SessionEnded ended => [new Published.SessionEnded(
            ended.PrincipalId, ended.SessionId, StoredEnum<SessionEndReason>.ToText(ended.Reason), ended.At.ToDateTimeOffset())],
        OwnerPendingPurged purged => [new PublishedOwners.OwnerPendingPurged(purged.OwnerId, purged.At.ToDateTimeOffset())],
        OwnerRegistered registered => [new PublishedOwners.OwnerRegistered(registered.OwnerId, registered.At.ToDateTimeOffset())],
        OwnerRegistrationAttempted attempted => [new PublishedOwners.OwnerRegistrationAttempted(
            attempted.Email, attempted.Language, attempted.At.ToDateTimeOffset())],
        OwnerCodeIssued issued => [new PublishedOwners.OwnerCodeIssued(
            issued.OwnerId, StoredEnum<CodePurpose>.ToText(issued.Purpose), issued.Email, issued.Name, issued.Language, issued.Code,
            issued.At.ToDateTimeOffset())],
        OwnerVerified verified => [new PublishedOwners.OwnerVerified(verified.OwnerId, verified.At.ToDateTimeOffset())],
        OwnerEmailChangeRequested requested => [new PublishedOwners.OwnerEmailChangeRequested(
            requested.OwnerId, requested.OldEmail, requested.NewEmailMasked, requested.Language, requested.Name, requested.At.ToDateTimeOffset())],
        OwnerEmailChanged changed => [new PublishedOwners.OwnerEmailChanged(changed.OwnerId, changed.At.ToDateTimeOffset())
        {
            OldEmail = changed.OldEmail,
            UndoToken = changed.UndoToken,
            Name = changed.Name,
            Language = changed.Language,
        }],
        OwnerEmailChangeUndone undone => [new PublishedOwners.OwnerEmailChangeUndone(undone.OwnerId, undone.At.ToDateTimeOffset())],
        _ => [],
    };
}
