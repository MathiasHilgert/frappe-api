using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Frappe.Migrations;

/// <summary>Applies every context's pending migrations, one context after another.</summary>
public static class MigrationsRunner
{
    /// <summary>
    /// Every context with migrations, in the order they are applied: Platform first, then the modules.
    /// </summary>
    public static IReadOnlyList<IDesignTimeDbContextFactory<FrappeDbContext>> Contexts { get; } =
    [new PlatformDbContextFactory()];

    /// <summary>
    /// Applies the pending migrations of each context in <see cref="Contexts"/> order and returns the IDs
    /// it applied; run it as the owner role.
    /// </summary>
    public static async Task<IReadOnlyList<string>> MigrateAllAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        // The runtime image ships no Kerberos library, so a GSS attempt fails and logs an error first.
        string connection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;
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
}
