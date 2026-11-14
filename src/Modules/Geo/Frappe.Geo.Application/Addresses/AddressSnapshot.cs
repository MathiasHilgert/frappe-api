using Frappe.Geo.Application.Territory;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// A validated, normalized address: the snapshot the module that needs an address stores as its own copy (S11). Geo
/// stores none. <paramref name="Reference"/> re-validates it later; <paramref name="CountryCode"/> (ISO 3166-1 alpha-2)
/// and <paramref name="SubdivisionCode"/> (ISO 3166-2) are <see langword="null"/> when Geo could not map them.
/// </summary>
public sealed record AddressSnapshot(
    string Reference,
    string FormattedAddress,
    string? Street,
    string? StreetNumber,
    string? PostalCode,
    string? Locality,
    string? CountryCode,
    string? SubdivisionCode,
    Coordinates? Coordinates);
