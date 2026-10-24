using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

public static class ChangePasswordHandler
{
    public static async Task<Throttled<bool>> Handle(
        ChangePassword command,
        PasswordReverifier reverifier,
        PasswordPolicy policy,
        IPasswordHasher hasher,
        IPrincipalRepository principals,
        CancellationToken cancellationToken)
    {
        var verified = await reverifier.VerifyAsync(
            command.PrincipalId, command.CurrentPassword, IdentityErrors.CurrentPasswordIncorrect, cancellationToken);
        if (verified.Result is not { } principal)
            return new Throttled<bool>(null, verified.RetryAfter);
        if (principal.IsFailure)
            return new Throttled<bool>(Result.Failure<bool>(principal.Errors[0]));

        var password = await policy.ValidateAsync(command.NewPassword, cancellationToken);
        if (password.IsFailure)
        {
            var notification = new Notification();
            foreach (var error in password.Errors)
                notification.Add(error with { Field = "newPassword" });
            return new Throttled<bool>(Result.Failure<bool>(notification));
        }

        principal.Value.ChangePassword(hasher.Hash(password.Value.Value), command.SessionId);
        var saved = await principals.SaveChangesAsync(cancellationToken);
        return new Throttled<bool>(saved.IsSuccess ? Result.Success(true) : Result.Failure<bool>(saved.Errors[0]));
    }
}
