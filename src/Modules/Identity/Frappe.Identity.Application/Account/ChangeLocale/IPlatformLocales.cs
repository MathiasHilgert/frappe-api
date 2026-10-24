using Frappe.SharedKernel.Localization;

namespace Frappe.Identity.Application.Account;

/// <summary>The locales the platform speaks, so a stored preference is always one it can answer in.</summary>
public interface IPlatformLocales
{
    /// <summary>The served locale for <paramref name="requested"/> (exact, a truncation or a same-language sibling), or null.</summary>
    Locale? Lookup(Locale requested);
}
