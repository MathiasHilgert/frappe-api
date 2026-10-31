using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Cities;

/// <summary>Reads the city catalogue from the database: a keyset list and a ranked locality autocomplete.</summary>
public interface ICityReader
{
    /// <summary>Lists one page of active cities in folded-name order, then by INDEC id.</summary>
    /// <param name="country">Only cities of this country, when given.</param>
    /// <param name="subdivision">Only cities of this province, when given.</param>
    /// <param name="after">The position of the last city of the previous page, or <see langword="null"/> for the first.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="locale">The requested locale of the province and country names.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with the locale its names resolved to.</returns>
    Task<Localized<CityPage>> ListAsync(
        CountryCode? country,
        SubdivisionCode? subdivision,
        CityKey? after,
        int limit,
        Locale locale,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Returns at most 10 active cities matching a query: exact name, then name prefix, then a word in the name, then
    /// words found only in the province or country name, then similar spellings.
    /// </summary>
    /// <param name="terms">The query.</param>
    /// <param name="country">Only cities of this country, when given.</param>
    /// <param name="subdivision">Only cities of this province, when given.</param>
    /// <param name="locale">The requested locale of the province and country names.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ranked cities, with the locale their names resolved to.</returns>
    Task<Localized<IReadOnlyList<CityDto>>> SearchAsync(
        CitySearchTerms terms,
        CountryCode? country,
        SubdivisionCode? subdivision,
        Locale locale,
        CancellationToken cancellationToken
    );
}
