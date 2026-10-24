using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Wrong codes count against <see cref="AttemptPolicies.EmailChangeConfirmFailuresPerPrincipal"/>; a right one clears
/// the count. Each attempt reserves its slot before the code is checked, so a parallel burst cannot get more guesses
/// than the limit; failures that are not guesses give the slot back.
/// </summary>
public static class ConfirmEmailChangeHandler
{
    private static readonly AttemptPolicy Policy = AttemptPolicies.EmailChangeConfirmFailuresPerPrincipal;

    private static readonly HashSet<string> CodeFailures = new(StringComparer.Ordinal)
    {
        IdentityErrors.CodeInvalid.Code, IdentityErrors.CodeExpired.Code, IdentityErrors.CodeAlreadyUsed.Code,
    };

    public static async Task<Result> Handle(
        ConfirmEmailChange command,
        IPrincipalRepository principals,
        ISecretHasher hasher,
        IAttemptLimiter limiter,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var reservation = await AttemptReservation.TakeAsync(limiter, cancellationToken, (Policy, command.PrincipalId.ToString()));
        if (!reservation.IsAllowed)
            return Result.Failure(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));

        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
        {
            await reservation.ReleaseAsync(cancellationToken);
            return Result.Failure(IdentityErrors.SessionNotFound);
        }

        var confirmed = principal.ConfirmEmailChange(command.ChangeId, hasher.Hash(command.Code ?? string.Empty), clock.GetUtcNow());
        if (confirmed.IsFailure)
        {
            if (!confirmed.Errors.Any(e => CodeFailures.Contains(e.Code)))
                await reservation.ReleaseAsync(cancellationToken);
            return Result.Failure(confirmed.Errors);
        }

        // The code was right either way, so the count clears even when the address turns out to be taken.
        await reservation.SucceededAsync(Policy, cancellationToken);

        // Another owner may have claimed the address meanwhile: the unique index answers EmailTaken.
        return await principals.SaveClaimingEmailAsync(cancellationToken);
    }
}
