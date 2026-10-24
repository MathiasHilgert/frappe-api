using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

public static class ChangeLocaleHandler
{
    /// <summary>Saves the locale.</summary>
    /// <returns>Success, or <see cref="IdentityErrors.SessionNotFound"/> when the principal is gone.</returns>
    public static async Task<Result> Handle(
        ChangeLocale command, IPrincipalRepository principals, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return Result.Failure(IdentityErrors.SessionNotFound);

        principal.ChangeLocale(command.Locale);
        return await principals.SaveChangesAsync(cancellationToken);
    }
}
