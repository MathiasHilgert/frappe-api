using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Health;

/// <summary>
/// How a capability declares what it depends on. Each registration that opens a connection (a database, a cache, a broker) adds its own check, so a new module brings its dependencies' health with it.
/// </summary>
public static class HealthRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// A dependency the process cannot work without: when its check fails, readiness fails (<c>Unhealthy</c>).
        /// Registering the same name twice keeps the first registration.
        /// </summary>
        public IServiceCollection AddCriticalDependency(string name, Func<IServiceProvider, IHealthCheck> check) =>
            services.AddDependency(name, check, HealthStatus.Unhealthy);

        /// <summary>A dependency the process can run without, at reduced capability: when its check fails, readiness reports <c>Degraded</c>.</summary>
        public IServiceCollection AddOptionalDependency(string name, Func<IServiceProvider, IHealthCheck> check) =>
            services.AddDependency(name, check, HealthStatus.Degraded);

        private IServiceCollection AddDependency(string name, Func<IServiceProvider, IHealthCheck> check, HealthStatus failureStatus)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(check);

            services.AddFrappeHealth();
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                if (options.Registrations.All(r => r.Name != name))
                {
                    options.Registrations.Add(new HealthCheckRegistration(name, check, failureStatus, [HealthProbes.Ready], CheckTimeout));
                }
            });
            return services;
        }

        /// <summary>The process checks every host has: alive while it runs, started once the host finished starting.</summary>
        public IServiceCollection AddFrappeHealth()
        {
            ArgumentNullException.ThrowIfNull(services);
            if (services.Any(d => d.ServiceType == typeof(StartupHealthCheck)))
            {
                return services;
            }

            services.AddSingleton<StartupHealthCheck>();
            services.AddHealthChecks()
                .AddCheck("process", () => HealthCheckResult.Healthy(), [HealthProbes.Live])
                .AddCheck<StartupHealthCheck>("started", tags: [HealthProbes.Startup]);
            return services;
        }
    }

    /// <summary>How long one check may take before it counts as failed.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);
}
