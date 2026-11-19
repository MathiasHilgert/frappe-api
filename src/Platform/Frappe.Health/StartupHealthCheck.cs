using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Frappe.Health;

/// <summary>Healthy once the host has started (every hosted service's StartAsync completed).</summary>
internal sealed class StartupHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(lifetime.ApplicationStarted.IsCancellationRequested
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The host is still starting."));
}
