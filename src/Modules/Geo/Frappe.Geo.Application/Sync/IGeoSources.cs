using Frappe.Geo.Application.Territory;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.Application.Sync;

/// <summary>
/// How a country package plugs its sources into Geo without knowing the pipeline: it names each source, the schedule
/// section an environment may override and the default schedule. The host composes the packages
/// (<c>AddGeoSyncs(geo =&gt; geo.AddArgentina())</c>); Geo's core never references a country.
/// </summary>
public interface IGeoSources
{
    /// <summary>For a package's own services, such as its options.</summary>
    IServiceCollection Services { get; }

    /// <summary>A source of a country's territory, synced as job <c>sync-{key}</c> on <c>{section}:Cron</c>.</summary>
    IGeoSources Territory<TSource>(string key, string section, string defaultCron)
        where TSource : class, ISnapshotSource<TerritorySnapshot>;
}
