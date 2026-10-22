using Frappe.Platform.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Frappe.Platform.Persistence;

public static class PersistenceRegistration
{
    public const string ConnectionStringName = "frappe";

    public static string GetPostgresConnectionString(this IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

    /// <summary>
    /// One container-owned <see cref="NpgsqlDataSource"/> (one pool, disposed on shutdown) shared by every module
    /// DbContext. Postgres is critical: it holds all state and the outbox.
    /// </summary>
    public static IServiceCollection AddPostgres(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddNpgsqlDataSource(configuration.GetPostgresConnectionString());

        services.AddHealthChecks().AddDependency("postgres", DependencyCriticality.Critical, async (sp, ct) =>
        {
            await using var command = sp.GetRequiredService<NpgsqlDataSource>().CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(ct);
        });

        return services;
    }

    /// <summary>
    /// Registers a module DbContext enrolled in Wolverine's transactional outbox, with its
    /// migrations history kept inside the module schema.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext =>
        services.AddDbContextWithWolverineIntegration<TContext>((sp, options) => options
            .UseNpgsql(
                sp.GetRequiredService<NpgsqlDataSource>(),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention());
}
