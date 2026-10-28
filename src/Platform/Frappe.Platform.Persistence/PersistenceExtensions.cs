using Frappe.Platform.Health;
using Frappe.Platform.Hosting;
using HealthChecks.NpgSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Platform.Persistence;

/// <summary>
/// PostgreSQL for every module: one data source, the <see cref="PlatformDbContext"/>, the registration
/// every module's context uses and the critical health check.
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>Registers the data source and the platform context; <c>ConnectionStrings:Frappe</c> is validated on start.</summary>
    public static TBuilder AddPersistence<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // The application name comes from the validated host role, so persistence registers it too.
        builder.AddHostRole();
        builder
            .Services.AddOptionsWithValidateOnStart<DatabaseOptions, DatabaseOptionsValidator>()
            .BindConfiguration(DatabaseOptions.SectionName);
        // One data source per process owns the one connection pool and the NodaTime type mappings. The
        // connection string is read from the validated options when the data source is first resolved,
        // so configuration added after registration, as test hosts add it, still applies.
        builder.Services.AddNpgsqlDataSource(connectionString: "", ConfigureDataSource);
        builder.Services.AddFrappeDbContext<PlatformDbContext>(PlatformDbContext.Schema);
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

    /// <summary>
    /// Registers <typeparamref name="TContext"/>, the context that owns <paramref name="schema"/>, on the
    /// one data source <see cref="AddPersistence"/> registers.
    /// </summary>
    public static IServiceCollection AddFrappeDbContext<TContext>(
        this IServiceCollection services,
        string schema
    )
        where TContext : FrappeDbContext =>
        // shortcut: plain AddDbContext without the Wolverine integration; the query-bus PR replaces it
        // with AddDbContextWithWolverineIntegration.
        services.AddDbContext<TContext>(
            (provider, options) =>
                options.UseFrappeDatabase(provider.GetRequiredService<NpgsqlDataSource>(), schema),
            optionsLifetime: ServiceLifetime.Singleton
        );

    private static void ConfigureDataSource(
        IServiceProvider services,
        NpgsqlDataSourceBuilder dataSource
    )
    {
        DatabaseOptions database = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        HostRole role = services.GetRequiredService<IOptions<PlatformOptions>>().Value.Role;
        dataSource.ConnectionStringBuilder.ConnectionString = database.Frappe;
        // The application name shows which role holds each connection in pg_stat_activity.
        dataSource.ConnectionStringBuilder.ApplicationName = ApplicationName(role);
        // Encryption to Postgres is TLS; the default GSS attempt needs a Kerberos library the
        // runtime image does not ship, so it fails and logs an error before falling back.
        dataSource.ConnectionStringBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
        // Every handler holds its connection for an explicit transaction, so multiplexing is never
        // allowed, whatever the connection string says.
        dataSource.ConnectionStringBuilder.Multiplexing = false;
        dataSource.UseNodaTime();
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
