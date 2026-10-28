using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Migrations;

/// <summary>Builds <see cref="PlatformDbContext"/> for the migrations tooling and the migrator.</summary>
public sealed class PlatformDbContextFactory()
    : FrappeDbContextFactory<PlatformDbContext>(PlatformDbContext.Schema)
{
    /// <inheritdoc />
    protected override PlatformDbContext Create(DbContextOptions<PlatformDbContext> options) =>
        new(options);
}
