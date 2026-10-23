using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Proves the current password, applies the password policy to the new one and lets the aggregate swap the hash,
/// revoking every session except the caller's in the same write.
/// </summary>
public sealed class ChangePassword(
    PasswordReverifier reverifier, PasswordPolicy policy, IPasswordHasher hasher, IPrincipalRepository principals)
{
    public async Task<Throttled<bool>> HandleAsync(
        AuthenticatedSession caller, string? currentPassword, string? newPassword, CancellationToken cancellationToken)
    {
        var verified = await reverifier.VerifyAsync(caller, currentPassword, IdentityErrors.CurrentPasswordIncorrect, cancellationToken);
        if (verified.Result is not { } principal)
            return new Throttled<bool>(null, verified.RetryAfter);
        if (principal.IsFailure)
            return new Throttled<bool>(Result.Failure<bool>(principal.Errors[0]));

        var password = await policy.ValidateAsync(newPassword, cancellationToken);
        if (password.IsFailure)
        {
            var notification = new Notification();
            foreach (var error in password.Errors)
                notification.Add(error with { Field = "newPassword" });
            return new Throttled<bool>(Result.Failure<bool>(notification));
        }

        principal.Value.ChangePassword(hasher.Hash(password.Value.Value), caller.SessionId);
        var saved = await principals.SaveChangesAsync(cancellationToken);
        return new Throttled<bool>(saved.IsSuccess ? Result.Success(true) : Result.Failure<bool>(saved.Errors[0]));
    }
}
