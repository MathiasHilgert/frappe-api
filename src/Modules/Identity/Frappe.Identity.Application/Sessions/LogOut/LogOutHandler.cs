using Frappe.Identity.Application.Principals;

namespace Frappe.Identity.Application.Sessions;

public static class LogOutHandler
{
    public static async Task Handle(LogOut command, IPrincipalRepository principals, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        // Already gone (revoked concurrently) is the outcome logout wants anyway.
        if (principal is null || principal.RevokeSession(command.SessionId).IsFailure)
            return;

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Revoking a session failed to save: {saved.Errors[0].Code}.");
    }
}
