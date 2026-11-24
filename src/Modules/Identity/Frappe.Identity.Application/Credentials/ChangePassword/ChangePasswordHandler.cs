using Frappe.Access.Contracts;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Credentials.ChangePassword;

/// <summary>
/// Handles <see cref="ChangePassword"/>. The current password is rechecked first (it is throttled per principal, so a stolen session cannot
/// guess it), and the new one is judged by the policy only after that, so the breach list is asked nothing for a caller who does not know the
/// password. The new hash, the rotated stamp and the revocations are one unit.
/// </summary>
internal sealed class ChangePasswordHandler(
    IPrincipals principals,
    IPasswordCredentials credentials,
    PasswordRecheck recheck,
    PasswordPolicy policy,
    IPasswordHasher hasher,
    CredentialChange change,
    TimeProvider time,
    ICaller caller) : ICommandHandler<ChangePassword, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangePassword command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var principal = await principals.FindAsync(caller.RequiredPrincipalId, cancellationToken);
        if (principal is null)
        {
            return new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        }

        var credential = await credentials.FindAsync(principal.Id, cancellationToken);
        var stored = credential is null ? null : new PasswordHash(credential.Hash);
        var matched = await recheck.MatchesAsync(principal.Id, stored, command.CurrentPassword, cancellationToken);
        if (!matched.IsSuccess)
        {
            return Result.Failure<Unit>(matched.Errors);
        }

        if (!matched.Value || credential is null)
        {
            return Error.RuleViolation(IdentityErrorCodes.PasswordIncorrect);
        }

        var accepted = await policy.AcceptAsync(command.NewPassword, cancellationToken);
        if (!accepted.IsSuccess)
        {
            return Result.Failure<Unit>(accepted.Errors);
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        credential.Change(hasher.Hash(accepted.Value), now);
        await change.ApplyAsync(principal, RevocationReason.PasswordChanged, caller.RequiredSessionId, cancellationToken);
        return Unit.Value;
    }
}
