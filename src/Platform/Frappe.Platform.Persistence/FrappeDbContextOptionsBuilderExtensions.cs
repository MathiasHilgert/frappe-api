using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Frappe.Platform.Persistence;

/// <summary>
/// The <see cref="FrappeDbContext"/> options the host and the migrations tooling share, so the model
/// migrations are generated from is the model the host runs.
/// </summary>
public static class FrappeDbContextOptionsBuilderExtensions
{
    /// <summary>The assembly that holds the migrations and the model snapshot.</summary>
    public const string MigrationsAssembly = "Frappe.Migrations";

    /// <summary>The schema of the migrations history table.</summary>
    public const string HistorySchema = "platform";

    /// <summary>The migrations history table.</summary>
    public const string HistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Configures PostgreSQL on <paramref name="dataSource"/>; without one, the connection string is
    /// set later, as the migrations tooling does. Every aggregate root's <c>Revision</c> becomes its
    /// concurrency token and is incremented once per save that changes the aggregate.
    /// </summary>
    public static DbContextOptionsBuilder UseFrappeDatabase(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource? dataSource
    ) =>
        (
            dataSource is null
                ? options.UseNpgsql(ConfigureNpgsql)
                : options.UseNpgsql(dataSource, ConfigureNpgsql)
        )
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IModelCustomizer, FrappeModelCustomizer>()
            .AddInterceptors(AggregateRevisionInterceptor.Instance);

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql
            .UseNodaTime()
            .MigrationsAssembly(MigrationsAssembly)
            .MigrationsHistoryTable(HistoryTable, HistorySchema);
}
