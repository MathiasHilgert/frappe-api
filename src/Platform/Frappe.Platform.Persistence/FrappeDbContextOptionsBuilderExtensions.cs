using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Frappe.Platform.Persistence;

/// <summary>
/// The <see cref="FrappeDbContext"/> options the host and the migrations tooling share, so the model
/// migrations are generated from is the model the host runs.
/// </summary>
public static class FrappeDbContextOptionsBuilderExtensions
{
    /// <summary>The assembly that holds every context's migrations and model snapshot.</summary>
    public const string MigrationsAssembly = "Frappe.Migrations";

    /// <summary>The migrations history table, one per context in the context's schema.</summary>
    public const string HistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Configures PostgreSQL on <paramref name="dataSource"/> for the context that owns
    /// <paramref name="schema"/>; without a data source, the connection string is set later, as the
    /// migrations tooling does. Every aggregate root's <c>Revision</c> is incremented once per save
    /// that changes the aggregate.
    /// </summary>
    public static DbContextOptionsBuilder UseFrappeDatabase(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource? dataSource,
        string schema
    ) =>
        (
            dataSource is null
                ? options.UseNpgsql(npgsql => ConfigureNpgsql(npgsql, schema))
                : options.UseNpgsql(dataSource, npgsql => ConfigureNpgsql(npgsql, schema))
        )
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(AggregateRevisionInterceptor.Instance);

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql, string schema) =>
        npgsql
            .UseNodaTime()
            .MigrationsAssembly(MigrationsAssembly)
            .MigrationsHistoryTable(HistoryTable, schema);
}
