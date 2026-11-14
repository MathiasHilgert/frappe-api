namespace Frappe.Geo.Contracts.Queries;

/// <summary>
/// A country: its ISO 3166-1 codes, its English name, its currencies in use, and what Geo covers for it: the years of
/// each holiday calendar it holds (<see cref="Holidays"/>), the business-day policies it answers and its time zone.
/// </summary>
public sealed record GeoCountry(
    string Code,
    string Alpha3,
    string Numeric,
    string Name,
    IReadOnlyList<string> Currencies,
    IReadOnlyList<GeoHolidayCoverage> Holidays,
    IReadOnlyList<BusinessDayPolicy> BusinessDayPolicies,
    string? TimeZone);
