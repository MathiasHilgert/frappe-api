using Frappe.Geo.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Migrations;

/// <summary>Builds <see cref="GeoDbContext"/> for the migrations tooling and the migrator.</summary>
public sealed class GeoDbContextFactory()
    : FrappeDbContextFactory<GeoDbContext>(GeoDbContext.Schema)
{
    /// <inheritdoc />
    protected override GeoDbContext Create(DbContextOptions<GeoDbContext> options) => new(options);
}
