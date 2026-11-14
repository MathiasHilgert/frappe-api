using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Frappe.Health;

/// <summary>
/// The three Kubernetes probes, as health check tags. Liveness checks only the process: a dependency outage must
/// never restart every replica at once. Readiness is the sum of the critical dependencies: one down takes the replica
/// out of traffic without restarting it. Startup says the host finished starting.
/// </summary>
public static class HealthProbes
{
    public const string Live = "live";
    public const string Ready = "ready";
    public const string Startup = "startup";
}

/// <summary>
/// How a capability declares what it depends on. Each registration that opens a connection (a module database, the
/// cache, the broker) adds its own check, so a new module brings its dependencies' health with it.
/// </summary>
public static class HealthRegistration
{
    /// <summary>How long one check may take before it counts as failed.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A dependency the process cannot work without: when its check fails, readiness fails (<c>Unhealthy</c>).
    /// Registering the same name twice keeps the first registration.
    /// </summary>
    public static IServiceCollection AddCriticalDependency(this IServiceCollection services, string name, Func<IServiceProvider, IHealthCheck> check) =>
        services.AddDependency(name, check, HealthStatus.Unhealthy);

    /// <summary>A dependency the process can work without, worse: when its check fails, readiness reports <c>Degraded</c>.</summary>
    public static IServiceCollection AddOptionalDependency(this IServiceCollection services, string name, Func<IServiceProvider, IHealthCheck> check) =>
        services.AddDependency(name, check, HealthStatus.Degraded);

    private static IServiceCollection AddDependency(
        this IServiceCollection services, string name, Func<IServiceProvider, IHealthCheck> check, HealthStatus failureStatus)
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
    public static IServiceCollection AddFrappeHealth(this IServiceCollection services)
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

/// <summary>Healthy once the host has started (every hosted service's StartAsync completed).</summary>
internal sealed class StartupHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(lifetime.ApplicationStarted.IsCancellationRequested
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The host is still starting."));
}
