namespace Frappe.Geo.Countries.Argentina.Banking;

/// <summary>Where the BCRA publishes each year's banking holidays: <c>Geo:Argentina:BankingHolidays:BaseUrl</c>.</summary>
public sealed class BankingHolidaysOptions
{
    public const string Section = "Geo:Argentina:BankingHolidays";

    /// <summary>The root of <c>consulta-feriados-bancarios-{year}/</c>. Default <c>https://www.bcra.gob.ar/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("https://www.bcra.gob.ar/");
}
