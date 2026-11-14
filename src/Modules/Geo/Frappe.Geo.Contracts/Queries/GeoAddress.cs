namespace Frappe.Geo.Contracts.Queries;

/// <summary>
/// A validated, normalized address: the snapshot the consuming module stores as its own copy (Geo stores none), with
/// <see cref="Reference"/> to re-validate it later. <see cref="CountryCode"/> is ISO 3166-1 alpha-2 and
/// <see cref="SubdivisionCode"/> ISO 3166-2, each <see langword="null"/> when Geo could not map it.
/// </summary>
public sealed record GeoAddress(
    string Reference,
    string FormattedAddress,
    string? Street,
    string? StreetNumber,
    string? PostalCode,
    string? Locality,
    string? CountryCode,
    string? SubdivisionCode,
    GeoCoordinates? Coordinates);
