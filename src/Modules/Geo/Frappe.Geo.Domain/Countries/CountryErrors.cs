namespace Frappe.Geo.Domain.Countries;

/// <summary>The errors of the country catalogue.</summary>
public static class CountryErrors
{
    /// <summary>Gets the error for an ISO 3166-1 alpha-2 code the catalogue does not hold.</summary>
    public static Error NotFound { get; } = Error.NotFound("geo.country.not_found");

    /// <summary>Gets the error for a country without a current tender, such as Antarctica (AQ).</summary>
    public static Error DefaultCurrencyUndefined { get; } =
        Error.Validation("geo.country.default_currency_undefined");
}
