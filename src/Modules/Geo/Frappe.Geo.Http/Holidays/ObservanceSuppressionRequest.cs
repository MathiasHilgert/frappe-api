namespace Frappe.Geo.Http.Holidays;

/// <summary>The source's observance to leave out: its <c>date</c> (<c>yyyy-MM-dd</c>) and <c>name</c>.</summary>
public sealed record ObservanceSuppressionRequest(string? Date, string? Name);
