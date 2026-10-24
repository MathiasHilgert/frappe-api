using Frappe.Identity.Application.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

public static class LogOutHandler
{
    public static async Task<Result> Handle(LogOut command, IPrincipalRepository principals, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        // Already gone (revoked concurrently) is the outcome logout wants anyway.
        if (principal is null || principal.RevokeSession(command.SessionId).IsFailure)
            return Result.Success();

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Revoking a session failed to save: {saved.Errors[0].Code}.");
        return Result.Success();
    }
}
