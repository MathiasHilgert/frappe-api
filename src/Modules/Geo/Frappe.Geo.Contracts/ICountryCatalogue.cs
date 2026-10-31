using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>Reads the ISO 3166-1 country catalogue, retired countries included.</summary>
public interface ICountryCatalogue
{
    /// <summary>Returns a country with its name in <paramref name="locale"/>.</summary>
    /// <param name="country">The country.</param>
    /// <param name="locale">The locale of the name; the nearest one along its fallback chain when not seeded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The country, or <c>geo.country.not_found</c>.</returns>
    ValueTask<ErrorOr<GeoCountry>> GetCountry(
        CountryCode country,
        Locale locale,
        CancellationToken cancellationToken
    );

    /// <summary>Returns the current tender of a country.</summary>
    /// <param name="country">The country.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The currency, <c>geo.country.not_found</c>, or <c>geo.country.default_currency_undefined</c> for a country
    /// without one.
    /// </returns>
    ValueTask<ErrorOr<CurrencyCode>> DefaultCurrencyOf(
        CountryCode country,
        CancellationToken cancellationToken
    );
}
