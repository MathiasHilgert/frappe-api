using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Re-checks the password of a signed-in owner under <see cref="AttemptPolicies.ReverifyFailuresPerPrincipal"/>:
/// only wrong passwords count, and a right one clears the count. Nothing is ever locked. The slot is reserved before
/// the password is checked, so parallel guesses cannot exceed the limit.
/// </summary>
public sealed class PasswordReverifier(IPrincipalRepository principals, IPasswordHasher hasher, IAttemptLimiter limiter)
{
    private static readonly AttemptPolicy Policy = AttemptPolicies.ReverifyFailuresPerPrincipal;

    /// <summary>Loads the caller's principal when <paramref name="password"/> is theirs; <paramref name="wrong"/> otherwise.</summary>
    public async Task<Result<Principal>> VerifyAsync(
        Guid principalId, string? password, Error wrong, CancellationToken cancellationToken)
    {
        var reservation = await AttemptReservation.TakeAsync(limiter, [(Policy, principalId.ToString())], cancellationToken);
        if (!reservation.IsAllowed)
            return Result.Failure<Principal>(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));

        var principal = await principals.GetByIdAsync(principalId, cancellationToken);
        if (principal is null)
        {
            await reservation.ReleaseAsync();
            return Result.Failure<Principal>(IdentityErrors.SessionNotFound);
        }

        // Argon2 cannot derive from an empty secret; an empty password is simply wrong (and keeps its slot).
        if (string.IsNullOrEmpty(password)
            || !await hasher.VerifyAsync(password, principal.Owner.PasswordHash, cancellationToken))
        {
            return Result.Failure<Principal>(wrong);
        }

        await reservation.SucceededAsync(Policy);
        return Result.Success(principal);
    }
}
