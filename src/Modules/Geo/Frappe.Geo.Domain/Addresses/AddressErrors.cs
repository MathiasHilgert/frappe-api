namespace Frappe.Geo.Domain.Addresses;

/// <summary>The errors of address validation.</summary>
public static class AddressErrors
{
    /// <summary>Gets the error for an address without a city.</summary>
    public static Error CityRequired { get; } = Error.Validation("geo.address.city_required");

    /// <summary>Gets the error for a postal code its country's pattern does not match as a whole.</summary>
    public static Error PostalCodeInvalid { get; } = Error.Validation("geo.postal_code.invalid");

    /// <summary>Gets the error for a missing postal code in a country that requires one.</summary>
    public static Error PostalCodeRequired { get; } = Error.Validation("geo.postal_code.required");
}
