using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>
/// Validates the parts of an address another module captures against the catalogue: its city and its postal code;
/// the street line is checked when its value object is built. Geo stores no address; each module keeps its own.
/// Every failure is a validation error, an unknown code included, so a caller can return them for its own input
/// fields as they are.
/// </summary>
public interface IAddressValidator
{
    /// <summary>Checks a postal code against its country's pattern only, never against a province.</summary>
    /// <param name="country">The country.</param>
    /// <param name="postalCode">The postal code.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// Success, or the validation error <c>geo.country.not_found</c> or <c>geo.postal_code.invalid</c>; a country
    /// without an address format accepts any code.
    /// </returns>
    ValueTask<ErrorOr<Success>> ValidatePostalCode(
        CountryCode country,
        PostalCode postalCode,
        CancellationToken cancellationToken
    );

    // TODO: optional coordinates captured once from the chosen address suggestion (deferred with the geocoding
    // provider; never inferred or backfilled)
    /// <summary>Checks the parts of an address, returning every failure at once.</summary>
    /// <param name="city">The city; the empty id is rejected.</param>
    /// <param name="streetLine">The street line, already checked by <see cref="StreetLine.Create"/>.</param>
    /// <param name="postalCode">The postal code, checked against the city's country, or null when the address carries none.</param>
    /// <param name="currentCity">The city the address holds now, which stays valid after it is retired; null for a new address.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// Success, or the validation errors among <c>geo.address.city_required</c>, <c>geo.city.not_found</c>,
    /// <c>geo.city.retired</c>, <c>geo.postal_code.invalid</c> and <c>geo.postal_code.required</c>.
    /// </returns>
    ValueTask<ErrorOr<Success>> ValidateAsync(
        CityId city,
        StreetLine streetLine,
        PostalCode? postalCode,
        CityId? currentCity,
        CancellationToken cancellationToken
    );
}
