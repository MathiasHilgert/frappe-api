namespace Frappe.Geo.Contracts.Queries;

/// <summary>The years Geo holds a country's calendar of one scope (<c>general</c>, <c>banking</c>) for.</summary>
public sealed record GeoHolidayCoverage(string Scope, IReadOnlyList<int> Years);
