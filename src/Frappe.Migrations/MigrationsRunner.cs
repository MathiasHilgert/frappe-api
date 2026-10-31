using Frappe.Geo.Infrastructure;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Frappe.Migrations;

/// <summary>Applies every context's pending migrations, then every module's reference data.</summary>
public static class MigrationsRunner
{
    /// <summary>
    /// Every context with migrations, in the order they are applied: Platform first, then the modules.
    /// </summary>
    public static IReadOnlyList<IDesignTimeDbContextFactory<FrappeDbContext>> Contexts { get; } =
    [new PlatformDbContextFactory(), new GeoDbContextFactory()];

    /// <summary>
    /// Every module, in the order it seeds: after the modules whose reference data its own rows reference.
    /// </summary>
    public static IReadOnlyList<IModule> Modules { get; } = [new GeoModule()];

    /// <summary>
    /// Applies the pending migrations of each context in <see cref="Contexts"/> order and returns the IDs
    /// it applied; run it as the owner role.
    /// </summary>
    public static async Task<IReadOnlyList<string>> MigrateAllAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        string connection = Owner(connectionString);
        List<string> applied = [];
        foreach (IDesignTimeDbContextFactory<FrappeDbContext> factory in Contexts)
        {
            await using FrappeDbContext db = factory.CreateDbContext([]);
            db.Database.SetConnectionString(connection);
            applied.AddRange(await db.Database.GetPendingMigrationsAsync(cancellationToken));
            await db.Database.MigrateAsync(cancellationToken);
        }

        return applied;
    }

    /// <summary>
    /// Runs each module's <see cref="IModule.SeedAsync"/> in order, each in its own transaction, and returns the
    /// rows they changed; run it as the owner role after <see cref="MigrateAllAsync"/>. A failing module rolls
    /// back its own rows and stops the run; the modules before it keep theirs.
    /// </summary>
    public static async Task<int> SeedAllAsync(
        string connectionString,
        IEnumerable<IModule> modules,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(modules);
        await using NpgsqlConnection connection = new(Owner(connectionString));
        await connection.OpenAsync(cancellationToken);
        int changed = 0;
        foreach (IModule module in modules)
        {
            // Npgsql runs every command of the connection in its open transaction.
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                cancellationToken
            );
            changed += await module.SeedAsync(connection, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return changed;
    }

    // The runtime image ships no Kerberos library, so a GSS attempt fails and logs an error first.
    private static string Owner(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;
}
