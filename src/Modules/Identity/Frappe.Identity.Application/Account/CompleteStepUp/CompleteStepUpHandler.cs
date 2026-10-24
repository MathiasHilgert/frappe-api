using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

public static class CompleteStepUpHandler
{
    /// <summary>Marks the session as stepped up when the password is right.</summary>
    /// <returns>When the step-up expires.</returns>
    public static async Task<Throttled<DateTimeOffset>> Handle(
        CompleteStepUp command, PasswordReverifier reverifier, IPrincipalRepository principals, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var verified = await reverifier.VerifyAsync(
            command.PrincipalId, command.Password, IdentityErrors.StepUpPasswordIncorrect, cancellationToken);
        if (verified.Result is not { } principal)
            return new Throttled<DateTimeOffset>(null, verified.RetryAfter);
        if (principal.IsFailure)
            return new Throttled<DateTimeOffset>(Result.Failure<DateTimeOffset>(principal.Errors[0]));

        var expiresAt = principal.Value.CompleteStepUp(command.SessionId, clock);
        if (expiresAt.IsSuccess)
            await principals.SaveChangesAsync(cancellationToken);
        return new Throttled<DateTimeOffset>(expiresAt);
    }
}
