using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>Reads the locale catalogue: the CLDR locales with modern coverage.</summary>
public interface ILocaleCatalogue
{
    /// <summary>Checks that a locale is offered: its exact canonical tag is an active row, with no fallback to its language.</summary>
    /// <param name="locale">The locale.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Success, or <c>geo.locale.not_found</c>.</returns>
    ValueTask<ErrorOr<Success>> Exists(Locale locale, CancellationToken cancellationToken);
}
