using Frappe.Geo.Application.Catalog;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Persistence;

/// <summary>
/// What the Migrator does for Geo: apply its migrations as the owner, then bring the catalog to <c>seed</c>. Its role is
/// granted data access afterwards.
/// </summary>
public static class GeoMigration
{
    /// <summary>The login role the Api and the Worker use for Geo.</summary>
    public const string Role = "frappe_geo";

    public static async Task MigrateAsync(string ownerConnectionString, ICatalogSeed seed, CancellationToken cancellationToken)
    {
        var options = GeoPersistenceRegistration.Configure(
            new DbContextOptionsBuilder<GeoDbContext>(),
            ownerConnectionString,
            new TimeProviderClock(TimeProvider.System),
            seed);
        await using var context = new GeoDbContext(options.Options);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
