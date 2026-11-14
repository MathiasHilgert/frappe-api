namespace Frappe.Geo.Application.Territory;

/// <summary>A representative point (WGS 84), such as the centroid the source publishes.</summary>
public sealed record Coordinates(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}
