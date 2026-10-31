using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Currencies;

/// <summary>Reads the currency catalogue.</summary>
public interface ICurrencyReader
{
    /// <summary>Lists the active currencies by their name in <paramref name="locale"/>, then by code.</summary>
    /// <param name="locale">The requested locale of the names.</param>
    /// <returns>The currencies, with the locale their names resolved to.</returns>
    Localized<IReadOnlyList<CurrencyDto>> List(Locale locale);
}
