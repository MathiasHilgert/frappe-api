using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Cities;

/// <summary>A city: a Georef locality with its official name and ready-made label.</summary>
/// <param name="CityId">The city id other modules store.</param>
/// <param name="Name">The official name, such as Pilar.</param>
/// <param name="CountryCode">The ISO 3166-1 alpha-2 code of its country.</param>
/// <param name="SubdivisionCode">The ISO 3166-2 code of its province.</param>
/// <param name="SubdivisionName">The province name in the requested locale.</param>
/// <param name="Label">
/// Name, province and country in the requested locale, such as Pilar, Buenos Aires, Argentina; with the department in
/// parentheses after the name when another city of the province has the same name.
/// </param>
/// <param name="TimeZoneId">The IANA zone of its province.</param>
public sealed record CityDto(
    CityId CityId,
    string Name,
    CountryCode CountryCode,
    SubdivisionCode SubdivisionCode,
    string SubdivisionName,
    string Label,
    TimeZoneId TimeZoneId
);
