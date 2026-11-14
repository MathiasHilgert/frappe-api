namespace Frappe.Geo.Http.Addresses;

/// <summary>The address's point (WGS 84).</summary>
public sealed record AddressCoordinatesResponse(double Latitude, double Longitude);
