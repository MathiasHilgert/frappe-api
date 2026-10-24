using Frappe.Identity.Application.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

public static class RevokeSessionHandler
{
    public static Task<Result> Handle(RevokeSession command, IPrincipalRepository principals, CancellationToken cancellationToken) =>
        SessionRevocation.RevokeAsync(principals, command.PrincipalId, command.SessionId, cancellationToken);
}
