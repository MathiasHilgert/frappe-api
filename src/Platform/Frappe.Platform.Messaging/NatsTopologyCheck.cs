using Frappe.Platform.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Wolverine.Configuration;
using Wolverine.Nats.Internal;
using Wolverine.Runtime;

namespace Frappe.Platform.Messaging;

// Runs once Wolverine has started (registered after it): every NATS endpoint must be a durable JetStream
// endpoint, and the stream must cap message size, the one limit Wolverine cannot declare.
internal sealed class NatsTopologyCheck(
    IWolverineRuntime runtime,
    IOptions<NatsOptions> nats,
    IOptions<PlatformOptions> platform,
    IHostEnvironment environment
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        NatsTransport transport = runtime.Options.Transports.GetOrCreate<NatsTransport>();
        CheckEndpoints(transport);
        // A publishing role may start before NATS answers; the listening roles check the stream.
        if (!NatsEvents.Listens(platform.Value.Role) || !IsConnected(transport))
        {
            return;
        }

        INatsJSContext jetStream = transport.JetStreamContext;
        INatsJSStream stream = await jetStream.GetStreamAsync(
            nats.Value.Stream,
            cancellationToken: cancellationToken
        );
        StreamConfig config = stream.Info.Config;
        if (config.MaxMsgSize == NatsTopology.MaxMessageSize)
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"JetStream stream '{config.Name}' accepts messages of {config.MaxMsgSize} bytes; it must cap them at "
                    + $"{NatsTopology.MaxMessageSize}. Run eng/nats/provision.sh against this server."
            );
        }

        await jetStream.UpdateStreamAsync(
            config with
            {
                MaxMsgSize = NatsTopology.MaxMessageSize,
            },
            cancellationToken
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Tests without NATS stub the transport, which then never opens a connection.
    private static bool IsConnected(NatsTransport transport)
    {
        try
        {
            return transport.Connection is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // A core NATS or buffered endpoint would lose an event on a crash; the reply subject Wolverine adds for
    // request/reply carries no event.
    private void CheckEndpoints(NatsTransport transport)
    {
        string[] unsafeEndpoints =
        [
            .. transport
                .Endpoints()
                .OfType<NatsEndpoint>()
                .Where(endpoint =>
                {
                    if (endpoint.IsUsedForReplies)
                    {
                        return false;
                    }

                    endpoint.Compile(runtime);
                    return !endpoint.UseJetStream || endpoint.Mode != EndpointMode.Durable;
                })
                .Select(static endpoint =>
                    $"{endpoint.Uri} ({endpoint.Mode}, JetStream {endpoint.UseJetStream})"
                ),
        ];
        if (unsafeEndpoints.Length > 0)
        {
            throw new InvalidOperationException(
                "Every NATS endpoint must be a durable JetStream endpoint, so the outbox and inbox cover each event; "
                    + $"these are not: {string.Join(", ", unsafeEndpoints)}."
            );
        }
    }
}
