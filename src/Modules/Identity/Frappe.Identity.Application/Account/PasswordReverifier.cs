using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Re-checks the password of a signed-in owner under <see cref="AttemptPolicies.ReverifyFailuresPerPrincipal"/>:
/// only wrong passwords count, and a right one clears the count. Nothing is ever locked.
/// </summary>
public sealed class PasswordReverifier(IPrincipalRepository principals, IPasswordHasher hasher, IAttemptLimiter limiter)
{
    private static readonly AttemptPolicy Policy = AttemptPolicies.ReverifyFailuresPerPrincipal;

    /// <summary>Loads the caller's principal when <paramref name="password"/> is theirs; <paramref name="wrong"/> otherwise.</summary>
    public async Task<Throttled<Principal>> VerifyAsync(
        AuthenticatedSession caller, string? password, Error wrong, CancellationToken cancellationToken)
    {
        var key = caller.PrincipalId.ToString();
        var gate = await limiter.CheckAsync(Policy, key, cancellationToken);
        if (!gate.IsAllowed)
            return new Throttled<Principal>(null, gate.RetryAfter);

        var principal = await principals.GetByIdAsync(caller.PrincipalId, cancellationToken);
        if (principal is null)
            return new Throttled<Principal>(Result.Failure<Principal>(IdentityErrors.SessionNotFound));

        if (!hasher.Verify(password ?? string.Empty, principal.Owner.PasswordHash))
        {
            await limiter.RecordAsync(Policy, key, cancellationToken);
            return new Throttled<Principal>(Result.Failure<Principal>(wrong));
        }

        await limiter.ResetAsync(Policy, key, cancellationToken);
        return new Throttled<Principal>(Result.Success(principal));
    }
}
