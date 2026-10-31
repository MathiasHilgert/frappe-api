using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Subdivisions;

/// <summary>Reads the subdivision catalogue.</summary>
public interface ISubdivisionReader
{
    /// <summary>Lists the active subdivisions of a country by their name in <paramref name="locale"/>, then by code.</summary>
    /// <param name="country">The country; one without subdivisions in the catalogue has an empty list.</param>
    /// <param name="locale">The requested locale of the names.</param>
    /// <returns>The subdivisions, with the locale their names resolved to.</returns>
    Localized<IReadOnlyList<SubdivisionDto>> List(CountryCode country, Locale locale);

    /// <summary>Finds one subdivision, retired ones included.</summary>
    /// <param name="subdivision">The subdivision.</param>
    /// <param name="locale">The requested locale of the name.</param>
    /// <returns>The subdivision with the locale its name resolved to, or <see langword="null"/> when not held.</returns>
    Localized<SubdivisionDto>? Find(SubdivisionCode subdivision, Locale locale);
}
