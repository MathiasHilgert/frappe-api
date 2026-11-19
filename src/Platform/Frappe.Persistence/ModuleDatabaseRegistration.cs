using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        /// Binds <see cref="DatabaseOptions"/> from section <c>Database</c> and validates them when the host starts; an
        /// invalid value stops it naming the key. Registered once however many databases a host adds; recognized by its own
        /// validator, so another <see cref="IValidateOptions{TOptions}"/> of the same options does not stop the binding.
        /// </summary>
        public IServiceCollection AddDatabaseOptions()
        {
            ArgumentNullException.ThrowIfNull(services);
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IValidateOptions<DatabaseOptions>)
                && descriptor.ImplementationType == typeof(DatabaseOptionsValidator)))
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
        /// says the rest (Postgres, schema).
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
                    configure(provider, options, ModuleConnectionString(provider, connectionString(provider))));
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
            return ModuleDatabase.WithConnectionLimits(connectionString, new DatabaseOptions { MaxPoolSize = 1, CommandTimeout = limits.CommandTimeout });
        }
    }

    /// <summary>
    /// <paramref name="connectionString"/> under the host's <see cref="DatabaseOptions"/>
    /// (<see cref="ModuleDatabase.WithConnectionLimits"/>), for the module's context.
    /// </summary>
    private static string ModuleConnectionString(IServiceProvider provider, string connectionString) =>
        ModuleDatabase.WithConnectionLimits(connectionString, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
}
