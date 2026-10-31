namespace Frappe.Geo.Application.Countries;

/// <summary>A part of an address that geo validates and a country may require.</summary>
public enum AddressField
{
    /// <summary>The street line, such as Av. Corrientes 1234.</summary>
    StreetLine,

    /// <summary>The city.</summary>
    City,

    /// <summary>The first-level subdivision, such as a province.</summary>
    Subdivision,

    /// <summary>The postal code.</summary>
    PostalCode,
}
