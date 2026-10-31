using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Locales;

/// <summary>Reads the locale catalogue.</summary>
public interface ILocaleReader
{
    /// <summary>Lists the active locales by their name in <paramref name="locale"/>, then by tag.</summary>
    /// <param name="locale">The requested locale of the names.</param>
    /// <returns>The locales, with the locale their names resolved to.</returns>
    Localized<IReadOnlyList<LocaleDto>> List(Locale locale);
}
