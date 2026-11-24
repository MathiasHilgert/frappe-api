using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.UndoEmailChange;

/// <summary>
/// Handles <see cref="UndoEmailChange"/>. The token is spent first and everything else is one unit with it, so a refusal (the owner is gone, or
/// another verified owner holds the old address now) leaves it as it was. A pending owner holding the old address is deleted. The tokens issued
/// after this one are invalidated: they were mailed to the address this undo replaces, which whoever changed it may control. Earlier tokens stay,
/// so the way back from an older change is never taken away. Every live code of the owner stops working and every session is revoked.
/// </summary>
internal sealed class UndoEmailChangeHandler(
    EmailChangeUndoTokens undoTokens,
    IEmailChangeUndos undos,
    IOneTimeCodes codes,
    IPrincipals principals,
    IOwners owners,
    IEmailIdentifiers identifiers,
    AddressClaim claim,
    CredentialChange change,
    TimeProvider time) : ICommandHandler<UndoEmailChange, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UndoEmailChange command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var undo = await undoTokens.FindLiveAsync(command.Token, cancellationToken);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var old = undo is null ? null : Email.Create(undo.OldAddress);
        if (undo is null || old is not { IsSuccess: true }
            || await LoadAsync(undo.PrincipalId, cancellationToken) is not var (principal, owner, current)
            || !await claim.TryAsync(principal.Id, old.Value, cancellationToken)
            || !undo.Redeem(now).IsSuccess)
        {
            return Error.RuleViolation(IdentityErrorCodes.UndoInvalid);
        }

        current.ChangeTo(old.Value, now);
        owner.RecordEmailChangeUndone(now);
        foreach (var live in await codes.LiveOfAsync(principal.Id, cancellationToken))
        {
            live.Invalidate(now);
        }

        foreach (var later in await undos.LiveIssuedAfterAsync(principal.Id, undo.IssuedAt, cancellationToken))
        {
            later.Invalidate(now);
        }

        await change.ApplyAsync(principal, RevocationReason.EmailChangeUndone, keptSessionId: null, cancellationToken);
        return Unit.Value;
    }

    /// <summary>The active owner of <paramref name="principalId"/> with the address in force, or <see langword="null"/>.</summary>
    private async Task<(Principal Principal, Owner Owner, EmailIdentifier Current)?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var principal = await principals.FindAsync(principalId, cancellationToken);
        var owner = principal is { Kind: PrincipalKind.Owner, Status: PrincipalStatus.Active }
            ? await owners.FindAsync(principal.Id, cancellationToken)
            : null;
        var current = owner is null ? null : await identifiers.FindOfAsync(principalId, cancellationToken);
        return principal is null || owner is null || current is null ? null : (principal, owner, current);
    }
}
