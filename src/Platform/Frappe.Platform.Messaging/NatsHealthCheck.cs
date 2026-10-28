using Frappe.Platform.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Wolverine.Nats.Internal;
using Wolverine.Runtime;

namespace Frappe.Platform.Messaging;

// Listening roles need NATS to be ready. A publishing role keeps serving while NATS is away (the outbox holds
// its events), so it reports Degraded and readiness stays green.
internal sealed class NatsHealthCheck(
    IWolverineRuntime runtime,
    IOptions<PlatformOptions> platform,
    IOptions<NatsOptions> nats,
    IHostEnvironment environment
) : IHealthCheck
{
    // JetStream's status for a stream that does not exist.
    private const int NotFound = 404;

    public async Task<HealthCheckResult> CheckHealthAsync(
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
            return HealthCheckResult.Healthy("The NATS transport is disabled.");
        }

        if (state != NatsConnectionState.Open)
        {
            return new HealthCheckResult(Unavailable(context), $"NATS is {state}.");
        }

        // A publishing role only logs a failed Verify, so a stream nobody provisioned shows here; Development
        // creates the stream itself.
        if (environment.IsDevelopment())
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            await transport.JetStreamContext.GetStreamAsync(
                nats.Value.Stream,
                cancellationToken: cancellationToken
            );
            return HealthCheckResult.Healthy();
        }
        catch (NatsJSApiException exception) when (exception.Error.Code == NotFound)
        {
            return HealthCheckResult.Degraded(
                $"JetStream stream '{nats.Value.Stream}' does not exist; run the deploy step's resource setup."
            );
        }
        catch (Exception exception)
            when (exception is NatsException
                || (
                    exception is OperationCanceledException
                    && !cancellationToken.IsCancellationRequested
                )
            )
        {
            // A JetStream hiccup (leader election, permissions, timeout) with the connection up: a publishing
            // role keeps serving like it does with NATS fully down.
            return new HealthCheckResult(
                Unavailable(context),
                "JetStream did not answer the stream lookup.",
                exception
            );
        }
    }

    private HealthStatus Unavailable(HealthCheckContext context) =>
        NatsEvents.Listens(platform.Value.Role)
            ? context.Registration.FailureStatus
            : HealthStatus.Degraded;
}
