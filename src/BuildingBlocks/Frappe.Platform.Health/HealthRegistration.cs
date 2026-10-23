using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Platform.Health;

/// <summary>
/// Probe routes. Dependencies are declared by whoever owns them through
/// <see cref="DependencyHealthChecks.AddDependency"/>; this only decides how they are exposed.
/// </summary>
public static class HealthRegistration
{
    public static IServiceCollection AddPlatformHealth(this IServiceCollection services)
    {
        services.AddHealthChecks();
        services.Configure<HealthCheckPublisherOptions>(options =>
        {
            options.Delay = TimeSpan.FromSeconds(5);
            options.Period = TimeSpan.FromSeconds(15);
            options.Predicate = check => check.Tags.Contains(HealthTags.Ready);
        });
        services.AddSingleton<IHealthCheckPublisher, HealthMetricsPublisher>();
        return services;
    }

    public static IEndpointRouteBuilder MapPlatformHealth(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process answers. Never checks dependencies, or a database blip would restart every pod.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();

        // Readiness: may this instance receive traffic? Critical dependency down -> 503.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Ready),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();

        return endpoints;
    }
}
