using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

public static class ChangeLocaleHandler
{
    /// <summary>
    /// Only a locale the platform serves is stored; a regional variant it lacks maps to a sibling (es-CL to es-AR), as
    /// negotiation would, while a language it does not speak at all is refused rather than silently stored as English.
    /// </summary>
    /// <returns>
    /// The locale stored; or the tag's own parse errors, <see cref="IdentityErrors.LocaleUnsupported"/>, or
    /// <see cref="IdentityErrors.OwnerNotFound"/> when the principal is gone.
    /// </returns>
    public static async Task<Result<Locale>> Handle(
        ChangeLocale command, IPrincipalRepository principals, IPlatformLocales platform, CancellationToken cancellationToken)
    {
        var parsed = Locale.Create(command.Locale);
        if (!parsed.TryGetValue(out var requested))
            return Result.Failure<Locale>(parsed.Errors);
        if (platform.Lookup(requested) is not { } locale)
            return Result.Failure<Locale>(IdentityErrors.LocaleUnsupported);

        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return Result.Failure<Locale>(IdentityErrors.OwnerNotFound);

        principal.ChangeLocale(locale);
        var saved = await principals.SaveAsync(cancellationToken);
        return saved.IsSuccess ? Result.Success(locale) : Result.Failure<Locale>(saved.Errors);
    }
}
