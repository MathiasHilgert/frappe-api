using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

public static class RevokeSessionHandler
{
    public static async Task<Result> Handle(
        RevokeSession command, IPrincipalRepository principals, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return Result.Failure(IdentityErrors.SessionNotFound);

        var revoked = principal.RevokeSession(command.SessionId);
        if (revoked.IsSuccess)
            await principals.SaveAsync(cancellationToken);
        return revoked;
    }
}
