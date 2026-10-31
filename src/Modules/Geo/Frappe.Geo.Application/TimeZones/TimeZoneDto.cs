using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.TimeZones;

/// <summary>An IANA time zone a branch can run in.</summary>
/// <param name="TimeZoneId">The tzdb id, such as America/Argentina/Cordoba.</param>
/// <param name="CountryCode">The ISO 3166-1 alpha-2 code of the zone's country, or null for a zone of no country.</param>
public sealed record TimeZoneDto(TimeZoneId TimeZoneId, CountryCode? CountryCode);
