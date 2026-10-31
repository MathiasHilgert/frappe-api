using System.Data.Common;
using System.Text.Json;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Application.TimeZones;
using Frappe.Geo.Contracts;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Countries.GetCountry;
using Frappe.Geo.Infrastructure.Countries.ListCountries;
using Frappe.Geo.Infrastructure.Currencies;
using Frappe.Geo.Infrastructure.Currencies.ListCurrencies;
using Frappe.Geo.Infrastructure.OpenApi;
using Frappe.Geo.Infrastructure.Seed;
using Frappe.Geo.Infrastructure.Serialization;
using Frappe.Geo.Infrastructure.TimeZones;
using Frappe.Geo.Infrastructure.TimeZones.ListTimeZones;
using Frappe.Platform.Http.OpenApi;
using Frappe.Platform.Http.RateLimiting;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Geo.Infrastructure;

/// <summary>
/// The geo module: the platform's seeded reference catalogues (time zones, currencies, countries), read-only at
/// runtime, served as anonymous cacheable reads and to other modules through the geo Contracts.
/// </summary>
public sealed class GeoModule : IModule
{
    /// <inheritdoc />
    public ModuleMetadata Metadata { get; } =
        new("geo", GeoDbContext.Schema, "geo", new EventIdRange(2000, 2999), [], [], []);

    /// <inheritdoc />
    public void AddServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        IServiceCollection services = builder.Services;
        services.AddFrappeDbContext<GeoDbContext>(Metadata.Schema);
        services.ConfigureHttpJsonOptions(static options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, GeoJsonContext.Default)
        );
        services.AddSingleton<ITimeZoneReader, TimeZoneSnapshot>();
        services.AddSingleton<CurrencySnapshot>();
        services.AddSingleton<ICurrencyReader>(static provider =>
            provider.GetRequiredService<CurrencySnapshot>()
        );
        services.AddSingleton<ICurrencyCatalogue>(static provider =>
            provider.GetRequiredService<CurrencySnapshot>()
        );
        services.AddSingleton<CountrySnapshot>();
        services.AddSingleton<ICountryReader>(static provider =>
            provider.GetRequiredService<CountrySnapshot>()
        );
        services.AddSingleton<ICountryCatalogue>(static provider =>
            provider.GetRequiredService<CountrySnapshot>()
        );
        services.AddSingleton<GeoCatalogueLoader>();
        services.AddHostedService(static provider =>
            provider.GetRequiredService<GeoCatalogueLoader>()
        );
        // The XML generator intercepts this AddOpenApi call in this compilation; the helper keeps only its options:
        // the module's XML descriptions and the schemas of its value objects.
        services.AddModuleOpenApiDescriptions(static scratch =>
            scratch.AddOpenApi(static options =>
                options.AddSchemaTransformer<GeoSchemaTransformer>()
            )
        );
    }

    /// <inheritdoc />
    public void ConfigureJson(JsonSerializerOptions options) => GeoJsonConverters.Add(options);

    /// <inheritdoc />
    public void MapEndpoints(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group
            .RequireRateLimiting(RateLimitPolicies.Public)
            .AddEndpointFilter<CatalogueCachingFilter>();
        // TODO: address suggestions and geocoding through a provider port (no provider in v1; revisit when a vendor
        // and its coordinate storage terms are chosen)
        ListTimeZonesEndpoint.Map(group);
        ListCurrenciesEndpoint.Map(group);
        ListCountriesEndpoint.Map(group);
        GetCountryEndpoint.Map(group);
    }

    /// <inheritdoc />
    public Task<int> SeedAsync(DbConnection connection, CancellationToken cancellationToken) =>
        GeoSeed.SeedAsync(connection, cancellationToken);
}
