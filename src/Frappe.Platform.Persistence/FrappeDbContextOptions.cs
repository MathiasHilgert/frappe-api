using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Frappe.Platform.Persistence;

/// <summary>
/// The <see cref="FrappeDbContext"/> options the host and the migrations tooling share, so the model
/// migrations are generated from is the model the host runs.
/// </summary>
public static class FrappeDbContextOptions
{
    /// <summary>The assembly that holds the migrations and the model snapshot.</summary>
    public const string MigrationsAssembly = "Frappe.Migrations";

    /// <summary>The schema of the migrations history table.</summary>
    public const string HistorySchema = "platform";

    /// <summary>The migrations history table.</summary>
    public const string HistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Configures PostgreSQL on <paramref name="dataSource"/>; without one, the connection string is
    /// set later, as the migrations tooling does.
    /// </summary>
    public static DbContextOptionsBuilder UseFrappeDatabase(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource? dataSource
    ) =>
        (
            dataSource is null
                ? options.UseNpgsql(ConfigureNpgsql)
                : options.UseNpgsql(dataSource, ConfigureNpgsql)
        ).UseSnakeCaseNamingConvention();

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql
            .UseNodaTime()
            .MigrationsAssembly(MigrationsAssembly)
            .MigrationsHistoryTable(HistoryTable, HistorySchema);
}
