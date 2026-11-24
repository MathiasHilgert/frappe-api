using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.ConfirmEmailChange;

/// <summary>
/// Handles <see cref="ConfirmEmailChange"/>. The code of the caller is redeemed under the guess limits and must be the one the change id names.
/// A pending owner holding the new address is deleted in the same unit (it never proved the address); a verified holder keeps it and the answer
/// is <see cref="IdentityErrorCodes.CodeInvalid"/>, and a holder that appears between the read and the commit is stopped by the unique index.
/// The new address, the revocation of the other sessions and every other live code of the owner are one unit with the redemption.
/// </summary>
internal sealed class ConfirmEmailChangeHandler(
    CodeRedeemer redeemer,
    IOneTimeCodes codes,
    IPrincipals principals,
    IOwners owners,
    IEmailIdentifiers identifiers,
    CredentialChange change,
    TimeProvider time) : ICommandHandler<ConfirmEmailChange, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ConfirmEmailChange command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Guid.TryParse(command.ChangeId, out var changeId))
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        var redeemed = await redeemer.RedeemAsync(
            command.PrincipalId, CodePurpose.EmailChange, command.Code, command.ClientAddress, cancellationToken);
        if (!redeemed.IsSuccess)
        {
            return Result.Failure<Unit>(redeemed.Errors);
        }

        // A failed result rolls the unit back: the redemption stands only when everything below succeeds.
        var email = Email.Create(redeemed.Value.Target);
        if (redeemed.Value.Id != changeId || !email.IsSuccess
            || await LoadAsync(command.PrincipalId, cancellationToken) is not var (principal, owner, current)
            || !await FreeAddressAsync(principal.Id, email.Value, cancellationToken))
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        current.ChangeTo(email.Value, now);
        owner.RecordEmailChanged(now);
        foreach (var live in await codes.LiveOfAsync(principal.Id, cancellationToken))
        {
            live.Invalidate(now);
        }

        await change.ApplyAsync(principal, RevocationReason.EmailChanged, command.SessionId, cancellationToken);
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

    /// <summary>
    /// Whether the caller may take <paramref name="email"/>: nobody holds it, or a pending owner does and is deleted with everything of theirs.
    /// </summary>
    private async Task<bool> FreeAddressAsync(Guid principalId, Email email, CancellationToken cancellationToken)
    {
        var holder = await identifiers.FindByAddressAsync(email.NormalizedAddress, cancellationToken);
        if (holder is null)
        {
            return true;
        }

        var squatter = holder.PrincipalId == principalId ? null : await owners.FindAsync(holder.PrincipalId, cancellationToken);
        if (squatter is not { VerifiedAt: null })
        {
            return false;
        }

        principals.Remove((await principals.FindAsync(holder.PrincipalId, cancellationToken))!);
        return true;
    }
}
