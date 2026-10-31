using System.Data.Common;
using Frappe.Geo.Application.Timezones.ListTimezones;
using Frappe.Geo.Infrastructure.Timezones;
using Frappe.Geo.Infrastructure.Timezones.ListTimezones;
using Frappe.Platform.Health;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Geo.Infrastructure;

/// <summary>
/// The <c>geo</c> module: platform-global, seeded, read-only reference catalogues, the same for every business.
/// </summary>
public sealed class GeoModule : IModule
{
    /// <inheritdoc />
    public ModuleMetadata Metadata { get; } =
        new("geo", GeoDbContext.Schema, "geo", new EventIdRange(1000, 1999), [], [], []);

    /// <inheritdoc />
    public void AddServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddFrappeDbContext<GeoDbContext>(Metadata.Schema);
        builder
            .Services.AddOptionsWithValidateOnStart<GeoOptions, GeoOptionsValidator>()
            .BindConfiguration(GeoOptions.SectionName);
        builder.Services.AddScoped<IListTimezonesQueryService, TimezoneQueryService>();
        // Critical: without the catalogue no branch's zone resolves, so the replica must not take traffic.
        builder
            .Services.AddHealthChecks()
            .AddDependency<TimezoneCatalogHealthCheck>("geo.timezones", Criticality.Critical);
    }

    /// <inheritdoc />
    public void MapEndpoints(RouteGroupBuilder group) => ListTimezonesEndpoint.Map(group);

    /// <inheritdoc />
    public Task<int> SeedAsync(DbConnection connection, CancellationToken cancellationToken) =>
        TimezoneSeed.UpsertAsync(connection, cancellationToken);
}
