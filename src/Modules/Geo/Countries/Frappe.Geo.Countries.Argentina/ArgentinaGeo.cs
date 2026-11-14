using Frappe.Geo.Application.Sync;
using Frappe.Geo.Countries.Argentina.Territory;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.Countries.Argentina;

/// <summary>
/// Argentina for the hosts to compose: its sources (<c>AddGeoSyncs(geo =&gt; geo.AddArgentina())</c>).
/// </summary>
public static class ArgentinaGeo
{
    /// <summary>The territory sync, Mondays at 04:00 by default (<c>Geo:Argentina:Territory:Cron</c>).</summary>
    public const string WeeklyTerritorySync = "0 0 4 ? * MON";

    public static IGeoSources AddArgentina(this IGeoSources geo)
    {
        ArgumentNullException.ThrowIfNull(geo);
        geo.Services.AddOptions<GeorefOptions>().BindConfiguration(GeorefOptions.Section)
            .Validate(o => o.BaseUrl.IsAbsoluteUri && o.BaseUrl.AbsoluteUri.EndsWith('/'), $"Configuration '{GeorefOptions.Section}:BaseUrl' must be an absolute URL ending in '/'.")
            .ValidateOnStart();
        return geo
            .Territory<GeorefTerritorySource>("ar-territory", "Geo:Argentina:Territory", WeeklyTerritorySync);
    }
}
