using Frappe.Messaging.Inbox;
using Frappe.Messaging.Outbox;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Persistence;

/// <summary>The Geo module's database: schema <c>geo</c>, nothing outside it.</summary>
public sealed class GeoDbContext(DbContextOptions<GeoDbContext> options) : DbContext(options)
{
    public const string Schema = "geo";

    /// <summary>The module name of Geo's namespaces (<c>Frappe.Geo.*</c>), which keys Geo's unit of work and its outbox.</summary>
    public const string Module = "geo";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        // The locality search (trigram word similarity). A trusted extension since Postgres 13: the database owner creates it.
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoDbContext).Assembly);
        modelBuilder.AddOutbox();
        modelBuilder.AddInbox();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.StoreLanguageTags();
    }
}
