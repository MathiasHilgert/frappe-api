using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>One step: the right password marks the caller's session as freshly stepped up.</summary>
public sealed class CompleteStepUp(PasswordReverifier reverifier, IPrincipalRepository principals, TimeProvider clock)
{
    public async Task<Throttled<DateTimeOffset>> HandleAsync(
        AuthenticatedSession caller, string? password, CancellationToken cancellationToken)
    {
        var verified = await reverifier.VerifyAsync(caller, password, IdentityErrors.StepUpPasswordIncorrect, cancellationToken);
        if (verified.Result is not { } principal)
            return new Throttled<DateTimeOffset>(null, verified.RetryAfter);
        if (principal.IsFailure)
            return new Throttled<DateTimeOffset>(Result.Failure<DateTimeOffset>(principal.Errors[0]));

        var expiresAt = principal.Value.CompleteStepUp(caller.SessionId, clock);
        if (expiresAt.IsSuccess)
            await principals.SaveChangesAsync(cancellationToken);
        return new Throttled<DateTimeOffset>(expiresAt);
    }
}
