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
            var notification = new Notification();
            foreach (var error in password.Errors)
                notification.Add(error with { Field = "newPassword" });
            return Result.Failure(notification);
        }

        principal.ChangePassword(hasher.Hash(password.Value.Value), command.SessionId);
        var saved = await principals.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? Result.Success() : Result.Failure(saved.Errors[0]);
    }
}
