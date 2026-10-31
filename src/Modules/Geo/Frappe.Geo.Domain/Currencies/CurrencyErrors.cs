namespace Frappe.Geo.Domain.Currencies;

/// <summary>The errors of the currency catalogue.</summary>
public static class CurrencyErrors
{
    /// <summary>Gets the error for an ISO 4217 code the catalogue does not hold.</summary>
    public static Error NotFound { get; } = Error.NotFound("geo.currency.not_found");

    /// <summary>Gets the error for a currency ISO 4217 defines without a minor unit, such as gold (XAU).</summary>
    public static Error MinorUnitUndefined { get; } =
        Error.Validation("geo.currency.minor_unit_undefined");
}
