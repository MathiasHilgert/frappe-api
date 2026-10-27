using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform;

/// <summary>
/// The platform defaults every Frappé process shares. <c>Program.cs</c> calls
/// <see cref="AddFrappePlatform{TBuilder}"/> before building and <see cref="MapFrappeEndpoints"/> after.
/// </summary>
public static class PlatformExtensions
{
    private const string LiveTag = "live";
    private const string CriticalTag = "critical";

    /// <summary>Registers the platform services; <c>Platform:Role</c> is validated when the host starts.</summary>
    public static TBuilder AddFrappePlatform<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddOptionsWithValidateOnStart<PlatformOptions, PlatformOptionsValidator>()
            .BindConfiguration(PlatformOptions.SectionName);
        builder.AddDefaultHealthChecks();
        return builder;
    }

    /// <summary>Maps the platform endpoints. Health is mapped in every role so every replica can be probed.</summary>
    public static WebApplication MapFrappeEndpoints(this WebApplication app)
    {
        app.MapDefaultHealthEndpoints();
        return app;
    }

    private static void AddDefaultHealthChecks(this IHostApplicationBuilder builder) =>
        builder
            .Services.AddHealthChecks()
            // A trivial check, so liveness proves only that the process answers.
            .AddCheck("self", static () => HealthCheckResult.Healthy(), [LiveTag]);

    private static void MapDefaultHealthEndpoints(this WebApplication app)
    {
        // Liveness never checks dependencies, so a failing dependency cannot cause restarts.
        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions { Predicate = static check => check.Tags.Contains(LiveTag) }
        );
        // Readiness takes the process out of the load balancer only when a critical dependency fails.
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = static check => check.Tags.Contains(CriticalTag) }
        );
    }
}
