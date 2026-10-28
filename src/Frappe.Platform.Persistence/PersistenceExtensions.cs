using Frappe.Platform.Health;
using Frappe.Platform.Hosting;
using HealthChecks.NpgSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Platform.Persistence;

/// <summary>
/// PostgreSQL for every module: one data source, <see cref="FrappeDbContext"/> and its critical health check.
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>Registers the data source and the context; <c>ConnectionStrings:Frappe</c> is validated on start.</summary>
    public static TBuilder AddPersistence<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // The application name comes from the validated host role, so persistence registers it too.
        builder.AddHostRole();
        builder
            .Services.AddOptionsWithValidateOnStart<DatabaseOptions, DatabaseOptionsValidator>()
            .BindConfiguration(DatabaseOptions.SectionName);
        // One data source per process owns the one connection pool and the NodaTime type mappings.
        builder.Services.AddSingleton(static services => BuildDataSource(services));
        // shortcut: plain AddDbContext without the Wolverine integration; the query-bus PR replaces it
        // with AddDbContextWithWolverineIntegration.
        builder.Services.AddDbContext<FrappeDbContext>(
            static (services, options) =>
                options
                    .UseNpgsql(
                        services.GetRequiredService<NpgsqlDataSource>(),
                        static npgsql => npgsql.UseNodaTime()
                    )
                    .UseSnakeCaseNamingConvention(),
            optionsLifetime: ServiceLifetime.Singleton
        );
        // Opens a connection from the registered data source and runs SELECT 1, never a business query.
        builder
            .Services.AddHealthChecks()
            .AddDependency(
                "postgres",
                Criticality.Critical,
                static services => new NpgSqlHealthCheck(
                    new NpgSqlHealthCheckOptions(services.GetRequiredService<NpgsqlDataSource>())
                )
            );
        return builder;
    }

    private static NpgsqlDataSource BuildDataSource(IServiceProvider services)
    {
        DatabaseOptions database = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        HostRole role = services.GetRequiredService<IOptions<PlatformOptions>>().Value.Role;
        NpgsqlDataSourceBuilder dataSource = new(database.Frappe);
        // The application name shows which role holds each connection in pg_stat_activity.
        dataSource.ConnectionStringBuilder.ApplicationName = ApplicationName(role);
        // Every handler holds a physical connection for its explicit transaction; multiplexing has
        // nothing to share.
        dataSource.ConnectionStringBuilder.Multiplexing = false;
        // Encryption to Postgres is TLS; the default GSS attempt needs a Kerberos library the
        // runtime image does not ship, so it fails and logs an error before falling back.
        dataSource.ConnectionStringBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
        dataSource.UseLoggerFactory(services.GetRequiredService<ILoggerFactory>());
        dataSource.UseNodaTime();
        return dataSource.Build();
    }

    private static string ApplicationName(HostRole role) =>
        role switch
        {
            HostRole.All => "frappe-all",
            HostRole.Api => "frappe-api",
            HostRole.Worker => "frappe-worker",
            HostRole.Scheduler => "frappe-scheduler",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, message: null),
        };
}
