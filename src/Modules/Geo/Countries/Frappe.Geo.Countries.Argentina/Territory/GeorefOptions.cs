namespace Frappe.Geo.Countries.Argentina.Territory;

/// <summary>Where Georef's full downloads are: <c>Geo:Argentina:Georef:BaseUrl</c>.</summary>
public sealed class GeorefOptions
{
    public const string Section = "Geo:Argentina:Georef";

    /// <summary>
    /// The downloads, never <c>apis.datos.gob.ar</c>, whose downloads redirect to plain HTTP. Default
    /// <c>https://infra.datos.gob.ar/georef/</c>.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("https://infra.datos.gob.ar/georef/");
}
