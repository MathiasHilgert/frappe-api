using Frappe.Identity.Application.Principals;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Signs the current session out. Other sessions of the same owner stay open.</summary>
public sealed class LogOut(IPrincipalRepository principals)
{
    public async Task ExecuteAsync(AuthenticatedSession current, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(current.PrincipalId, cancellationToken);
        // Already gone (revoked concurrently) is the outcome logout wants anyway.
        if (principal is null || principal.RevokeSession(current.SessionId).IsFailure)
            return;

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Revoking a session failed to save: {saved.Errors[0].Code}.");
    }
}
