using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Infrastructure.Configuration;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Currencies;
using Frappe.Health;
using Frappe.Observability;
using Frappe.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.Infrastructure;

/// <summary>Composition of the Geo module for the hosts.</summary>
public static class GeoModule
{
    /// <summary>
    /// Use cases, their storage and the module database, bound from <c>ConnectionStrings:Geo</c>
    /// (<see cref="GeoDatabaseOptions"/>), with its readiness check. Shared by
    /// every host that runs Geo. A missing connection string fails at startup naming the key.
    /// </summary>
    public static IServiceCollection AddGeoCore(this IServiceCollection services)
    {
        services.AddOptions<GeoDatabaseOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(GeoDatabaseOptions.ConnectionStringName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), $"Configuration 'ConnectionStrings:{GeoDatabaseOptions.ConnectionStringName}' is required.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock>(provider => new TimeProviderClock(provider.GetRequiredService<TimeProvider>()));
        services.AddGeoPersistence(GeoConnectionString);
        services.AddCriticalDependency("geo-database", provider => new DatabaseHealthCheck(() => new Npgsql.NpgsqlConnection(GeoConnectionString(provider))));
        services.AddScoped<ICountryCatalog, EfCountryCatalog>();
        services.AddScoped<ICurrencyCatalog, EfCurrencyCatalog>();
        services.AddHandlers(typeof(ICountryCatalog).Assembly);
        return services;
    }

    private static string GeoConnectionString(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<GeoDatabaseOptions>>().Value.ConnectionString!;
}
