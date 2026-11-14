using Frappe.Geo.Application;
using Frappe.Geo.Domain.Holidays;
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

    /// <summary>
    /// Saves, telling a lost race over a holiday calendar apart: a calendar changed by someone else since it was read
    /// (its row version moved) is a <see cref="ConcurrentChangeException"/>, which Geo's adapters answer as a conflict.
    /// Any other failure (the outbox relay's, or a territory sync's) passes through unchanged.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(e => e.Entity is HolidayCalendar or CalendarEntry))
        {
            throw new ConcurrentChangeException("Someone else changed the same data first.", exception);
        }
    }

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
