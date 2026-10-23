using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// The application-level check behind sensitive operations: the caller's own session completed a step-up less than
/// five minutes ago. HTTP endpoints get it through <c>RequireStepUp()</c>; handlers can call it directly.
/// </summary>
public sealed class StepUpRequirement(IPrincipalRepository principals, TimeProvider clock)
{
    public async Task<bool> IsSatisfiedAsync(AuthenticatedSession caller, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(caller.PrincipalId, cancellationToken);
        return principal?.HasFreshStepUp(caller.SessionId, clock) ?? false;
    }
}
