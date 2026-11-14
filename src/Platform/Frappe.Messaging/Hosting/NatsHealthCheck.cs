using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;

namespace Frappe.Messaging.Hosting;

/// <summary>NATS answers a PING on the process's own connection.</summary>
internal sealed class NatsHealthCheck(NatsConnection nats) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await nats.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NatsException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "NATS does not answer.", exception);
        }
    }
}
