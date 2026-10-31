namespace Frappe.Geo.Infrastructure.Countries;

/// <summary>A row of <c>geo.country_names</c>: a country's name in one locale.</summary>
internal sealed class CountryNameRow
{
    public string CountryCode { get; init; } = null!;

    public string Locale { get; init; } = null!;

    public string Name { get; init; } = null!;
}
