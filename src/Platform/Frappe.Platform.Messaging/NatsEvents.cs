using Frappe.Kernel.Messaging;
using Frappe.Platform.Hosting;
using Frappe.Platform.Modularity;
using JasperFx;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream.Models;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Nats.Configuration;

namespace Frappe.Platform.Messaging;

// Every integration event travels outbox -> JetStream -> inbox. Resolved by Wolverine after every module is
// registered, so the listeners match the consumers the host actually runs.
internal sealed class NatsEvents(
    IOptions<NatsOptions> nats,
    IOptions<PlatformOptions> platform,
    IHostEnvironment environment,
    IEnumerable<IModule> modules
) : IWolverineExtension
{
    // Roles that listen; the others only publish.
    public static bool Listens(HostRole role) => role is HostRole.All or HostRole.Worker;

    // The integration events a registered module consumes; one durable consumer reads each.
    public static IEnumerable<Type> ConsumedEvents(IEnumerable<IModule> modules) =>
        modules
            .SelectMany(ModuleTypes.Handlers)
            .SelectMany(ModuleTypes.HandlerInterfaces)
            .Where(static contract =>
                contract.GetGenericTypeDefinition() == typeof(IIntegrationEventConsumer<>)
            )
            .Select(static contract => contract.GenericTypeArguments[0])
            .Distinct();

    public void Configure(WolverineOptions options)
    {
        NatsOptions settings = nats.Value;
        HostRole role = platform.Value.Role;
        // Commands stay on durable local queues; the api role runs no agents for the cluster.
        options.Durability.LeadershipEligible = role != HostRole.Api;
        DeclareTopology(options, settings);

        // The only route of an integration event, in every role; local handlers receive it from NATS.
        options
            .PublishMessagesToNatsSubject<IIntegrationEvent>(integrationEvent =>
                NatsTopology.Subject(settings.SubjectRoot, integrationEvent.GetType())
            )
            .UseJetStream(settings.Stream);
        // TODO: later PRs add CloudEvents ce-* headers, a MAX_DELIVERIES advisory stream, events.event_log,
        // the claim check for large payloads, circuit breakers and per-module replay tooling.

        if (Listens(role))
        {
            Listen(options, settings);
            return;
        }

        // A publishing role starts while NATS is unreachable: the outbox keeps every event and its sending
        // agent delivers them once NATS answers. One connection attempt, then startup continues, and no
        // listener starts, not even Wolverine's request/reply subject, which needs the connection. The same
        // setting would let a broken message store pass too; MessageStoreCheck fails the start for it.
        options.DisableAllExternalListeners = true;
        options.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;
        options.BrokerInitializationTimeout = TimeSpan.Zero;
    }

    private void DeclareTopology(WolverineOptions options, NatsOptions settings) =>
        options
            .UseNats(settings.Url)
            .ConfigureNatsOpts(opts =>
                settings.Creds is { Length: > 0 } creds
                    ? opts with
                    {
                        AuthOpts = opts.AuthOpts with { Creds = creds },
                    }
                    : opts
            )
            // Deployed processes run with restricted credentials and only check the topology that
            // eng/nats/provision.sh creates; startup fails listing every difference.
            .Provisioning(
                environment.IsDevelopment()
                    ? NatsProvisioning.CreateOrUpdate
                    : NatsProvisioning.Verify
            )
            .UseJetStream(defaults =>
            {
                defaults.AckWait = NatsTopology.AckWait;
                defaults.MaxDeliver = NatsTopology.MaxDeliver;
                // Not editable once the consumer exists; a new consumer starts at new events, and a backfill
                // is an explicit operation.
                defaults.DeliverPolicy = ConsumerConfigDeliverPolicy.New;
                defaults.DuplicateWindow = NatsTopology.DuplicateWindow;
            })
            // max_msg_size has no setting here: NatsTopologyCheck covers it.
            .DefineStream(
                settings.Stream,
                stream =>
                {
                    stream.Subjects = [$"{settings.SubjectRoot}.>"];
                    stream.Retention = StreamConfigRetention.Limits;
                    stream.Storage = StreamConfigStorage.File;
                    stream.DiscardPolicy = StreamConfigDiscard.Old;
                    stream.MaxAge = NatsTopology.MaxAge;
                    stream.MaxBytes = NatsTopology.MaxBytes;
                    stream.MaxMessages = -1;
                    stream.Replicas = settings.Replicas;
                    stream.DuplicateWindow = NatsTopology.DuplicateWindow;
                }
            );

    private void Listen(WolverineOptions options, NatsOptions settings)
    {
        foreach (Type consumed in ConsumedEvents(modules))
        {
            options
                .ListenToNatsSubject(NatsTopology.Subject(settings.SubjectRoot, consumed))
                .UseJetStream(
                    settings.Stream,
                    NatsTopology.Consumer(settings.ConsumerPrefix, consumed)
                )
                .DeliverFrom(ConsumerConfigDeliverPolicy.New)
                // Only a publisher other than the application can omit Wolverine's message-type header, and NATS
                // account permissions keep the subject root to the application. Without this, Wolverine acks and
                // drops such a message; the subject names the event, so it is read as that event instead and
                // fails like any other bad envelope (no tenant: dead-lettered).
                .DefaultIncomingMessage(consumed)
                .MaximumParallelMessages(NatsTopology.ListenerParallelism);
        }
    }
}
