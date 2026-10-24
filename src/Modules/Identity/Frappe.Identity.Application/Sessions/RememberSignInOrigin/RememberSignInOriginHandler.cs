using Frappe.Identity.Application.Security;

namespace Frappe.Identity.Application.Sessions;

public static class RememberSignInOriginHandler
{
    public static Task Handle(RememberSignInOrigin command, IKnownSignInOrigins knownOrigins, CancellationToken cancellationToken) =>
        knownOrigins.RememberAsync(command.Email, command.ClientIp, cancellationToken);
}
