namespace Frappe.Geo.Http.Holidays;

/// <summary>An observance (by date and name) of a jurisdiction's year whose manual override won over a change of the source.</summary>
public sealed record OverrideConflictResponse(string Jurisdiction, int Year, string Date, string Name);
