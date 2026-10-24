using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Wrong codes count against <see cref="AttemptPolicies.EmailChangeConfirmFailuresPerPrincipal"/>; a right one clears
/// the count.
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
        var key = command.PrincipalId.ToString();
        var gate = await limiter.CheckAsync(Policy, key, cancellationToken);
        if (!gate.IsAllowed)
            return Result.Failure(IdentityErrors.TooManyAttempts.WithRetryAfter(gate.RetryAfter));

        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return Result.Failure(IdentityErrors.SessionNotFound);

        var confirmed = principal.ConfirmEmailChange(command.ChangeId, hasher.Hash(command.Code ?? string.Empty), clock);
        if (confirmed.IsFailure)
        {
            if (confirmed.Errors.Any(e => CodeFailures.Contains(e.Code)))
                await limiter.RecordAsync(Policy, key, cancellationToken);
            return Result.Failure(confirmed.Errors[0]);
        }

        // Another owner may have claimed the address meanwhile: the unique index answers EmailTaken.
        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            return Result.Failure(saved.Errors[0]);

        await limiter.ResetAsync(Policy, key, cancellationToken);
        return Result.Success();
    }
}
