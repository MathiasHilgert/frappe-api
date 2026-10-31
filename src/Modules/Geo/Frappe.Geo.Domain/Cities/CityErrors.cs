namespace Frappe.Geo.Domain.Cities;

/// <summary>The errors of the city catalogue.</summary>
public static class CityErrors
{
    /// <summary>Gets the error for a city id the catalogue does not hold.</summary>
    public static Error NotFound { get; } = Error.NotFound("geo.city.not_found");

    /// <summary>Gets the error for a retired city chosen anew, rather than kept as the address's current city.</summary>
    public static Error Retired { get; } = Error.Validation("geo.city.retired");

    /// <summary>Gets the error for a city search that is not 2 to 100 characters, or that comes with a cursor.</summary>
    public static Error QueryInvalid { get; } = Error.Validation("geo.city.query_invalid");
}
