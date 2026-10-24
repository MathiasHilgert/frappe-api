using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

public static class ChangePasswordHandler
{
    public static async Task<Result> Handle(
        ChangePassword command,
        PasswordReverifier reverifier,
        PasswordPolicy policy,
        IPasswordHasher hasher,
        IPrincipalRepository principals,
        CancellationToken cancellationToken)
    {
        var verified = await reverifier.VerifyAsync(
            command.PrincipalId, command.CurrentPassword, IdentityErrors.CurrentPasswordIncorrect, cancellationToken);
        if (verified.IsFailure)
            return Result.Failure(verified.Errors[0]);
        var principal = verified.Value;

        var password = await policy.ValidateAsync(command.NewPassword, cancellationToken);
        if (password.IsFailure)
        {
            var notification = new ValidationNotification();
            foreach (var error in password.Errors)
                notification.Add(error with { Field = "newPassword" });
            return Result.Failure(notification);
        }

        principal.ChangePassword(await hasher.HashAsync(password.Value.Value, cancellationToken), command.SessionId);
        return await principals.SaveAsync(cancellationToken);
    }
}
