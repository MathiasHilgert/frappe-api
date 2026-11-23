using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace Frappe.Scheduling;

/// <summary>The scheduler is running: started, not in standby and not shut down, so triggers fire on this replica.</summary>
public sealed class SchedulerHealthCheck(ISchedulerFactory factory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scheduler = await factory.GetScheduler(cancellationToken);
        var status = await scheduler.GetStatus(cancellationToken);
        return status == SchedulerStatus.Running
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(
                context.Registration.FailureStatus,
                $"The scheduler is {status}, not running.",
                data: new Dictionary<string, object> { ["status"] = status.ToString() });
    }
}
