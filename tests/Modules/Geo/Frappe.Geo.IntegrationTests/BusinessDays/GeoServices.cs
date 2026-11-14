using Frappe.Caching;
using Frappe.Geo.Countries.Argentina;
using Frappe.Geo.Infrastructure;
using Frappe.Geo.Infrastructure.Messaging;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.IntegrationTests.BusinessDays;

/// <summary>
/// Geo's services as another module in the Api or the Worker gets them, over the same database and cache as
/// <see cref="GeoApi"/>: the public queries and the calendar cache's eviction, driven here without NATS.
/// </summary>
internal static class GeoServices
{
    public static ServiceProvider Build(GeoDatabase database, ValkeyCache valkey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Geo"] = database.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMetrics();
        services.AddFrappeCaching(valkey.ConnectionString, database.Token)
            .AddGeoCore()
            .AddArgentinaCalendar()
            .AddGeoCalendarCacheHandlers();
        return services.BuildServiceProvider();
    }
}
