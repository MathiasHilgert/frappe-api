using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Countries;

/// <summary>Reads the country catalogue.</summary>
public interface ICountryReader
{
    /// <summary>Lists the active countries by their name in <paramref name="locale"/>, then by code.</summary>
    /// <param name="locale">The requested locale of the names.</param>
    /// <returns>The countries, with the locale their names resolved to.</returns>
    Localized<IReadOnlyList<CountryDto>> List(Locale locale);

    /// <summary>Finds one country with its address format and status, retired ones included.</summary>
    /// <param name="country">The country.</param>
    /// <param name="locale">The requested locale of the name.</param>
    /// <returns>The country with the locale its name resolved to, or <see langword="null"/> when not held.</returns>
    Localized<CountryDetailDto>? Find(CountryCode country, Locale locale);
}
