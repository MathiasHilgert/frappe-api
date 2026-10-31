namespace Frappe.Geo.Domain.Locales;

/// <summary>The errors of the locale catalogue.</summary>
public static class LocaleErrors
{
    /// <summary>Gets the error for a locale tag that is not an active row of the catalogue.</summary>
    public static Error NotFound { get; } = Error.NotFound("geo.locale.not_found");
}
