namespace Frappe.Geo.Http.Territory;

/// <summary>A representative point (WGS 84) published by the source.</summary>
public sealed record CentroidResponse(double Latitude, double Longitude);
