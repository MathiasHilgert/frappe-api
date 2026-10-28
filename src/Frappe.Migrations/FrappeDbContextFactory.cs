using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Frappe.Migrations;

/// <summary>
/// Builds a context for the migrations tooling and the migrator, with the options the host uses. It
/// holds no connection string: <c>dotnet ef</c> takes the owner's through <c>--connection</c>, and code
/// sets it with <c>Database.SetConnectionString</c>.
/// </summary>
/// <param name="schema">The schema the context owns.</param>
public abstract class FrappeDbContextFactory<TContext>(string schema)
    : IDesignTimeDbContextFactory<TContext>
    where TContext : FrappeDbContext
{
    /// <inheritdoc />
    public TContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<TContext> options = new();
        options.UseFrappeDatabase(dataSource: null, schema);
        return Create(options.Options);
    }

    /// <summary>Creates the context with the built options.</summary>
    protected abstract TContext Create(DbContextOptions<TContext> options);
}
