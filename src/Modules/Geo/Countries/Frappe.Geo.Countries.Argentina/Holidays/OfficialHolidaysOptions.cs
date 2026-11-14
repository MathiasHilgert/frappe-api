namespace Frappe.Geo.Countries.Argentina.Holidays;

/// <summary>Where the national government publishes the holiday calendars: <c>Geo:Argentina:Holidays:BaseUrl</c>.</summary>
public sealed class OfficialHolidaysOptions
{
    public const string Section = "Geo:Argentina:Holidays";

    /// <summary>The folder of <c>holidays-{year}-es.json</c>. Default <c>https://www.argentina.gob.ar/sites/default/files/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("https://www.argentina.gob.ar/sites/default/files/");
}
