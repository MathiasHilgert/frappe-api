namespace Frappe.Geo.Application.Countries;

/// <summary>The years Geo holds a country's calendar of one scope (<c>general</c>, <c>banking</c>) for, in order.</summary>
public sealed record HolidayCoverage(string Scope, IReadOnlyList<int> Years);
