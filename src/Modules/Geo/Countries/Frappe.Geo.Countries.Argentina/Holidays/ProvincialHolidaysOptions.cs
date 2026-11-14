namespace Frappe.Geo.Countries.Argentina.Holidays;

/// <summary>Where Calendarios Nacionales publishes the provincial calendars: <c>Geo:Argentina:ProvincialHolidays:BaseUrl</c>.</summary>
public sealed class ProvincialHolidaysOptions
{
    public const string Section = "Geo:Argentina:ProvincialHolidays";

    /// <summary>The root of <c>{year}/provincias/{slug}.json</c>. Default <c>https://calendariosnacionales.com/ar/v1/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("https://calendariosnacionales.com/ar/v1/");
}
