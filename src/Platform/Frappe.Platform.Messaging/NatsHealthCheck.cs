using Frappe.Platform.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using Wolverine.Nats.Internal;
using Wolverine.Runtime;

namespace Frappe.Platform.Messaging;

// Listening roles need NATS to be ready. A publishing role keeps serving while NATS is away (the outbox holds
// its events), so it reports Degraded and readiness stays green.
internal sealed class NatsHealthCheck(IWolverineRuntime runtime, IOptions<PlatformOptions> platform)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        NatsTransport transport = runtime.Options.Transports.GetOrCreate<NatsTransport>();
        NatsConnectionState state;
        try
        {
            state = transport.Connection.ConnectionState;
        }
        catch (InvalidOperationException)
        {
            // Tests without NATS stub the transport.
            return Task.FromResult(HealthCheckResult.Healthy("The NATS transport is disabled."));
        }

        if (state == NatsConnectionState.Open)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        HealthStatus status = NatsEvents.Listens(platform.Value.Role)
            ? context.Registration.FailureStatus
            : HealthStatus.Degraded;
        return Task.FromResult(new HealthCheckResult(status, $"NATS is {state}."));
    }
}
