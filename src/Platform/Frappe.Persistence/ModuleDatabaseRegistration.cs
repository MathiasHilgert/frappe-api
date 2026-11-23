using Frappe.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// How a host registers a module database: always under the connection limits of <see cref="DatabaseOptions"/>, so no
/// module decides its own pool size or timeout.
/// </summary>
public static class ModuleDatabaseRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Everything a module's database needs in one call: its connection string (<c>ConnectionStrings:&lt;Module&gt;</c>,
        /// <see cref="ModuleDatabaseOptions"/>, validated when the host starts), the <see cref="TimeProvider"/> and
        /// <see cref="IClock"/> every module shares, <typeparamref name="TDbContext"/> in <paramref name="schema"/> under the
        /// connection limits, its unit of work (<see cref="UnitOfWorkRegistration.AddUnitOfWork"/>), the readiness check
        /// <c>&lt;module&gt;-database</c> (critical: a database that does not answer takes the process out of rotation) and
        /// the meter factory. <paramref name="configure"/> adds what only the module knows, such as its outbox.
        /// </summary>
        /// <param name="module">The lower-case module name of its namespaces, <c>Frappe.&lt;Module&gt;.*</c>.</param>
        /// <param name="schema">The Postgres schema the module owns; it also holds the module's migrations history.</param>
        /// <param name="configure">Adds to the context's options, with the host's services (the clock, for one).</param>
        /// <exception cref="InvalidOperationException"><typeparamref name="TDbContext"/> is already registered.</exception>
        public IServiceCollection AddModuleDatabase<TDbContext>(string module, string schema,
            Action<IServiceProvider, DbContextOptionsBuilder>? configure = null)
            where TDbContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(module);
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);
            if (services.Any(descriptor => descriptor.ServiceType == typeof(TDbContext)))
            {
                throw new InvalidOperationException($"The database of {typeof(TDbContext).Name} is already registered.");
            }

            var connectionName = ModuleDatabaseOptions.ConnectionStringName(module);
            string ConnectionString(IServiceProvider provider) =>
                provider.GetRequiredService<IOptionsMonitor<ModuleDatabaseOptions>>().Get(module).ConnectionString!;

            services.AddOptions<ModuleDatabaseOptions>(module)
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.Module = module;
                    options.ConnectionString = configuration.GetConnectionString(connectionName);
                })
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ModuleDatabaseOptions>, ModuleDatabaseOptionsValidator>());
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock>(provider => new TimeProviderClock(provider.GetRequiredService<TimeProvider>()));
            services.AddModuleDbContext<TDbContext>(ConnectionString, (provider, options, connection) =>
            {
                options.UseModulePostgres(connection, schema);
                configure?.Invoke(provider, options);
            });
            services.AddUnitOfWork<TDbContext>(module);
            services.AddCriticalDependency($"{module}-database", provider =>
                new DatabaseHealthCheck(() => new NpgsqlConnection(provider.ReadinessConnectionString(ConnectionString(provider)))));
            services.AddMetrics();
            return services;
        }

        /// <summary>
        /// Binds <see cref="DatabaseOptions"/> from section <c>Database</c> and validates them when the host starts; an
        /// invalid value stops it naming the key. Registered once however many databases a host adds.
        /// </summary>
        public IServiceCollection AddDatabaseOptions()
        {
            ArgumentNullException.ThrowIfNull(services);
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IValidateOptions<DatabaseOptions>)))
            {
                return services;
            }

            services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>());
            return services;
        }

        /// <summary>
        /// Registers <typeparamref name="TContext"/> over <paramref name="connectionString"/> under the connection limits
        /// (<see cref="ModuleConnectionString"/>); <paramref name="configure"/> receives the limited connection string and
        /// says the rest (Postgres, schema, outbox).
        /// </summary>
        public IServiceCollection AddModuleDbContext<TContext>(Func<IServiceProvider, string> connectionString,
            Action<IServiceProvider, DbContextOptionsBuilder, string> configure)
            where TContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(connectionString);
            ArgumentNullException.ThrowIfNull(configure);
            return services
                .AddDatabaseOptions()
                .AddDbContextPool<TContext>((provider, options) =>
                {
                    configure(provider, options, ModuleConnectionString(provider, connectionString(provider)));
                    if (provider.GetKeyedService<CommitRejectionMap>(typeof(TContext)) is { } rejections)
                    {
                        options.UseCommitRejections(rejections);
                    }
                });
        }
    }

    extension(IServiceProvider provider)
    {
        /// <summary>
        /// The connection string a module database's readiness check opens: the host's command timeout, and a pool of one
        /// connection (unless the connection string sets <c>Maximum Pool Size</c> itself). EF Core builds its own data source,
        /// so a plain <c>NpgsqlConnection</c> never shares the context's pool even on the same string; a check runs one
        /// <c>SELECT 1</c> at a time, so one connection is all it needs, and the connection budget can count it exactly.
        /// </summary>
        public string ReadinessConnectionString(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var limits = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return ModuleDatabase.WithConnectionLimits(
                connectionString,
                new DatabaseOptions { MaxPoolSize = 1, CommandTimeout = limits.CommandTimeout });
        }
    }

    /// <summary>
    /// <paramref name="connectionString"/> under the host's <see cref="DatabaseOptions"/>
    /// (<see cref="ModuleDatabase.WithConnectionLimits"/>), for the module's context.
    /// </summary>
    private static string ModuleConnectionString(IServiceProvider provider, string connectionString) =>
        ModuleDatabase.WithConnectionLimits(connectionString, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
}
