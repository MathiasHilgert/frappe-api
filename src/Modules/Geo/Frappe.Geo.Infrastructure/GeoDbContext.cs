using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure;

/// <summary>The context of the <c>geo</c> schema: the global reference catalogues and their translated names.</summary>
/// <param name="options">The options built by the platform for this context.</param>
public sealed class GeoDbContext(DbContextOptions<GeoDbContext> options)
    : FrappeDbContext(options, Schema)
{
    /// <summary>The schema this context owns.</summary>
    public const string Schema = "geo";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoDbContext).Assembly);
    }
}
