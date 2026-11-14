using Frappe.Geo.Application.Territory;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// A place's address as the provider gives it, in canonical parts: <paramref name="CountryCode"/> as the provider writes
/// it (Geo checks it is ISO 3166-1), and <paramref name="SubdivisionNames"/> the names of its first-level subdivision,
/// most complete first, which Geo maps to an ISO 3166-2 code through its names and aliases.
/// </summary>
public sealed record ProviderAddress(
    string ProviderId,
    string FormattedAddress,
    string? Street,
    string? StreetNumber,
    string? PostalCode,
    string? Locality,
    string? CountryCode,
    IReadOnlyList<string> SubdivisionNames,
    Coordinates? Coordinates);
