using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

public static class CompleteStepUpHandler
{
    /// <summary>Marks the session as stepped up when the password is right.</summary>
    /// <returns>When the step-up expires.</returns>
    public static async Task<Result<DateTimeOffset>> Handle(
        CompleteStepUp command, PasswordReverifier reverifier, IPrincipalRepository principals, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var verified = await reverifier.VerifyAsync(
            command.PrincipalId, command.Password, IdentityErrors.StepUpPasswordIncorrect, cancellationToken);
        if (!verified.TryGetValue(out var principal))
            return Result.Failure<DateTimeOffset>(verified.Errors[0]);

        var expiresAt = principal.CompleteStepUp(command.SessionId, clock);
        if (expiresAt.IsFailure)
            return expiresAt;

        var saved = await principals.SaveAsync(cancellationToken);
        return saved.IsSuccess ? expiresAt : Result.Failure<DateTimeOffset>(saved.Errors[0]);
    }
}
