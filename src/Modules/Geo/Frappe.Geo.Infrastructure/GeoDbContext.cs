using Frappe.Geo.Domain.Timezones;
using Frappe.Geo.Infrastructure.Timezones;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure;

/// <summary>The context of the <c>geo</c> schema: platform-global reference catalogues, read-only at runtime.</summary>
/// <param name="options">The options built by <see cref="FrappeDbContextOptionsBuilderExtensions.UseFrappeDatabase"/>.</param>
public sealed class GeoDbContext(DbContextOptions<GeoDbContext> options)
    : FrappeDbContext(options, Schema)
{
    /// <summary>The schema this context owns.</summary>
    public const string Schema = "geo";

    internal DbSet<Timezone> Timezones => Set<Timezone>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new TimezoneConfiguration());
    }
}
