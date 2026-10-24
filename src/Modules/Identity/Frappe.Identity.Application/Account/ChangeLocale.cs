using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.SharedKernel.Localization;

namespace Frappe.Identity.Application.Account;

/// <summary>Stores the signed-in owner's language. The caller already checked the platform speaks it.</summary>
public sealed class ChangeLocale(IPrincipalRepository principals, GetMe getMe)
{
    /// <summary>Saves <paramref name="locale"/> as the owner's language.</summary>
    /// <returns>The updated profile, or <see langword="null"/> when the principal is gone.</returns>
    public async Task<OwnerProfile?> HandleAsync(AuthenticatedSession caller, Locale locale, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(caller.PrincipalId, cancellationToken);
        if (principal is null)
            return null;

        principal.ChangeLocale(locale);
        var saved = await principals.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await getMe.ExecuteAsync(caller, cancellationToken) : null;
    }
}
