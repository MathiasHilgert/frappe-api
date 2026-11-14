namespace Frappe.Geo.Http.Countries;

/// <summary>The years Geo holds a country's calendar of one scope for, in order.</summary>
public sealed record HolidayCoverageResponse(string Scope, IReadOnlyList<int> Years);
