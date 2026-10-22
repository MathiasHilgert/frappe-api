using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Platform.Health;

/// <summary>How any module declares an external dependency the instance needs to be ready.</summary>
public static class DependencyHealthChecks
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Registers a readiness check that passes when <paramref name="ping"/> completes and fails when it throws
    /// or exceeds <paramref name="timeout"/>.
    /// </summary>
    public static IHealthChecksBuilder AddDependency(
        this IHealthChecksBuilder builder,
        string name,
        DependencyCriticality criticality,
        Func<IServiceProvider, CancellationToken, Task> ping,
        TimeSpan? timeout = null)
    {
        var critical = criticality == DependencyCriticality.Critical;

        return builder.Add(new HealthCheckRegistration(
            name,
            services => new PingHealthCheck(ct => ping(services, ct)),
            failureStatus: critical ? HealthStatus.Unhealthy : HealthStatus.Degraded,
            tags: [HealthTags.Ready, critical ? HealthTags.Critical : HealthTags.NonCritical],
            timeout: timeout ?? DefaultTimeout));
    }

    private sealed class PingHealthCheck(Func<CancellationToken, Task> ping) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await ping(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            // A timeout cancels the token: let it propagate so the health service reports the timeout itself.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
            }
        }
    }
}
