using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>Reads the ISO 4217 currency catalogue, retired currencies included.</summary>
public interface ICurrencyCatalogue
{
    /// <summary>Returns the number of decimal digits an amount in <paramref name="currency"/> carries.</summary>
    /// <param name="currency">The currency.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The minor unit, <c>geo.currency.not_found</c> for a code the catalogue does not hold, or
    /// <c>geo.currency.minor_unit_undefined</c> for a currency without one.
    /// </returns>
    ValueTask<ErrorOr<MinorUnit>> MinorUnitOf(
        CurrencyCode currency,
        CancellationToken cancellationToken
    );
}
