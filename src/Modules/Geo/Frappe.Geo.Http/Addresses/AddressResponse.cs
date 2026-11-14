namespace Frappe.Geo.Http.Addresses;

/// <summary>
/// A validated, normalized address: the snapshot the client's module stores as its own copy (Geo stores none), with the
/// reference that re-validates it later. The ISO codes are null when Geo could not map them.
/// </summary>
public sealed record AddressResponse(
    string Reference,
    string FormattedAddress,
    string? Street,
    string? StreetNumber,
    string? PostalCode,
    string? Locality,
    string? CountryCode,
    string? SubdivisionCode,
    AddressCoordinatesResponse? Coordinates);
