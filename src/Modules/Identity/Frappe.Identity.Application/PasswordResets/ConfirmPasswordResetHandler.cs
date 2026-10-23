using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// Sets a new password proven by the reset code; the aggregate revokes every session. Only wrong codes count against
/// the limits, and a blocked reset stays blocked even for the right code, so six digits cannot be brute-forced.
/// </summary>
public static class ConfirmPasswordResetHandler
{
    public static async Task<PasswordResetConfirmed> Handle(
        ConfirmPasswordReset command,
        IPrincipalRepository principals,
        IAttemptLimiter limiter,
        PasswordPolicy passwordPolicy,
        IPasswordHasher passwordHasher,
        ISecretHasher secretHasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var resetKey = command.ResetId.ToString();
        var perReset = await limiter.CheckAsync(AttemptPolicies.PasswordResetConfirmFailuresPerReset, resetKey, cancellationToken);
        var perIp = await limiter.CheckAsync(AttemptPolicies.PasswordResetConfirmFailuresPerIp, command.ClientIp, cancellationToken);
        if (!perReset.IsAllowed || !perIp.IsAllowed)
            return new(Result.Success(), perReset.RetryAfter > perIp.RetryAfter ? perReset.RetryAfter : perIp.RetryAfter);

        var password = await passwordPolicy.ValidateAsync(command.NewPassword, cancellationToken);
        if (password.IsFailure)
            return new(password, TimeSpan.Zero);

        var principal = await principals.GetByCodeIdAsync(command.ResetId, CodePurpose.PasswordReset, cancellationToken);
        var reset = principal is null || string.IsNullOrEmpty(command.Code)
            ? Result.Failure(IdentityErrors.CodeInvalid)
            : principal.ResetPassword(secretHasher.Hash(command.Code), passwordHasher.Hash(password.Value.Value), clock);

        if (reset.IsFailure)
        {
            await limiter.RecordAsync(AttemptPolicies.PasswordResetConfirmFailuresPerReset, resetKey, cancellationToken);
            await limiter.RecordAsync(AttemptPolicies.PasswordResetConfirmFailuresPerIp, command.ClientIp, cancellationToken);
            return new(reset, TimeSpan.Zero);
        }

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Saving a password reset failed: {saved.Errors[0].Code}.");
        await limiter.ResetAsync(AttemptPolicies.PasswordResetConfirmFailuresPerReset, resetKey, cancellationToken);
        return new(Result.Success(), TimeSpan.Zero);
    }
}
