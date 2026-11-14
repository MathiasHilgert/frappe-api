using Frappe.Geo.Application.Catalog;
using Frappe.Geo.Infrastructure.Persistence;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Geo.Infrastructure;

public static class GeoPersistenceRegistration
{
    /// <summary>
    /// Registers the Geo database with the module's own connection string (its own Postgres role), and Geo's unit of
    /// work over it: every Geo command commits its changes once, through the platform's command pipeline.
    /// </summary>
    internal static IServiceCollection AddGeoPersistence(this IServiceCollection services, Func<IServiceProvider, string> connectionString) =>
        services
            .AddDbContext<GeoDbContext>((provider, options) => Configure(
                options,
                connectionString(provider),
                provider.GetRequiredService<IClock>()))
            .AddUnitOfWork<GeoDbContext>(GeoDbContext.Module);

    /// <summary>
    /// The one place that says how a Geo context is built: Postgres in schema <c>geo</c>, plus the
    /// <paramref name="seed"/> its migrations apply (only the host that migrates passes one).
    /// </summary>
    public static TBuilder Configure<TBuilder>(TBuilder options, string connectionString, IClock clock, ICatalogSeed? seed = null)
        where TBuilder : DbContextOptionsBuilder
    {
        options
            .UseModulePostgres(connectionString, GeoDbContext.Schema);
        return seed is null ? options : options.UseGeoSeeding(seed, clock);
    }
}
