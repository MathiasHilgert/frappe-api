using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>A city of the catalogue: a Georef locality with its official name.</summary>
/// <param name="Id">The city id.</param>
/// <param name="Name">The official name, such as Villa María.</param>
/// <param name="Country">The country the city is in.</param>
/// <param name="Subdivision">The province the city is in.</param>
/// <param name="TimeZone">The IANA zone of the city's province.</param>
/// <param name="Status">Whether the city is in use or retired.</param>
public sealed record GeoCity(
    CityId Id,
    string Name,
    CountryCode Country,
    SubdivisionCode Subdivision,
    TimeZoneId TimeZone,
    ReferenceStatus Status
);
