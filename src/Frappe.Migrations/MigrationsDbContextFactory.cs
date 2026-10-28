using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Frappe.Migrations;

/// <summary>
/// Builds <see cref="FrappeDbContext"/> for the migrations tooling and the bundle. It holds no connection
/// string: <c>dotnet ef</c> and the bundle take the owner's through <c>--connection</c>, and code sets it
/// with <c>Database.SetConnectionString</c>.
/// </summary>
public sealed class MigrationsDbContextFactory : IDesignTimeDbContextFactory<FrappeDbContext>
{
    /// <inheritdoc />
    public FrappeDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<FrappeDbContext> options = new();
        options.UseFrappeDatabase(dataSource: null);
        return new FrappeDbContext(options.Options);
    }
}
