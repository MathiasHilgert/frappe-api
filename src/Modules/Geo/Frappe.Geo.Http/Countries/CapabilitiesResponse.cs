namespace Frappe.Geo.Http.Countries;

/// <summary>
/// What Geo covers for a country: each holiday calendar it holds (<c>general</c>, <c>banking</c>) with its years, the
/// business-day policies it answers (<c>banking</c>, <c>labor</c>, <c>commercial</c>) and its reference time zone (IANA;
/// null without business-day rules). Empty lists mean Geo has no data, never that the country has no holidays.
/// </summary>
public sealed record CapabilitiesResponse(IReadOnlyList<HolidayCoverageResponse> Holidays, IReadOnlyList<string> BusinessDayPolicies, string? TimeZone);
