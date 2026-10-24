using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

public static class LogOutHandler
{
    public static async Task<Result> Handle(LogOut command, IPrincipalRepository principals, CancellationToken cancellationToken)
    {
        var revoked = await SessionRevocation.RevokeAsync(principals, command.PrincipalId, command.SessionId, cancellationToken);
        // Already gone (revoked concurrently) is the outcome logout wants anyway.
        return revoked.IsFailure && revoked.Errors[0].Is(IdentityErrors.SessionNotFound) ? Result.Success() : revoked;
    }
}
