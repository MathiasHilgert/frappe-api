using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// Sets a new password proven by the reset code; the aggregate revokes every session. Each attempt reserves its slot
/// first and only wrong codes keep it, so six digits cannot be brute-forced, not even by a parallel burst; a blocked
/// reset stays blocked even for the right code. The code is proven before the password policy (a breach lookup) or
/// Argon2 run, so a guess costs the server nothing expensive.
/// </summary>
public static class ConfirmPasswordResetHandler
{
    public static async Task<Result> Handle(
        ConfirmPasswordReset command,
        IPrincipalRepository principals,
        IAttemptLimiter limiter,
        PasswordPolicy passwordPolicy,
        IPasswordHasher passwordHasher,
        ISecretHasher secretHasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var reservation = await AttemptReservation.TakeAsync(limiter, cancellationToken,
            (AttemptPolicies.PasswordResetConfirmFailuresPerReset, command.ResetId.ToString()),
            (AttemptPolicies.PasswordResetConfirmFailuresPerIp, command.ClientIp));
        if (!reservation.IsAllowed)
            return Result.Failure(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));

        var principal = await principals.GetByCodeIdAsync(command.ResetId, CodePurpose.PasswordReset, cancellationToken);
        var codeHash = string.IsNullOrEmpty(command.Code) ? null : secretHasher.Hash(command.Code);
        var proven = principal is null || codeHash is null
            ? Result.Failure(IdentityErrors.CodeInvalid)
            : principal.CheckCode(CodePurpose.PasswordReset, codeHash, clock);
        if (proven.IsFailure)
            return proven; // A wrong code keeps its reserved slots.

        // From here on the code is right: whatever happens, the attempt is not a guess.
        await reservation.SucceededAsync(AttemptPolicies.PasswordResetConfirmFailuresPerReset, cancellationToken);

        var password = await passwordPolicy.ValidateAsync(command.NewPassword, cancellationToken);
        if (password.IsFailure)
            return password;

        var reset = principal!.ResetPassword(
            codeHash!, await passwordHasher.HashAsync(password.Value.Value, cancellationToken), clock);
        if (reset.IsFailure)
            return reset;

        await principals.SaveAsync(cancellationToken);
        return Result.Success();
    }
}
