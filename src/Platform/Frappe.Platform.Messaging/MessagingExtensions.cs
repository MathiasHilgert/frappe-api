using Frappe.Kernel.Messaging;
using Frappe.Kernel.Tenancy;
using Frappe.Platform.Health;
using Frappe.Platform.Hosting;
using Frappe.Platform.Persistence;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Metrics;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence;
using Wolverine.Postgresql;

namespace Frappe.Platform.Messaging;

/// <summary>
/// The Wolverine bus every module dispatches through: durable local queues, the PostgreSQL outbox and inbox
/// in the <c>wolverine</c> schema, one error policy and the Kernel adapters.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>The schema of the message store; its tables come from a migration, never from the app.</summary>
    public const string Schema = "wolverine";

    /// <summary>The service name Wolverine tags its telemetry with; its meter is <c>Wolverine:frappe</c>.</summary>
    public const string ServiceName = "frappe";

    /// <summary>The parallelism of every local queue.</summary>
    public const int LocalQueueParallelism = 4;

    // The tags Wolverine's instruments may keep; tenant.id is left out, so no tenant id reaches a metric.
    private static readonly string[] _wolverineMetricTags =
    [
        "message.type",
        "message.destination",
        "exception.type",
        "source",
        "schedule.name",
        "database",
        "server",
        "lease.loss.stage",
    ];

    /// <summary>
    /// Registers the bus. Call it before <c>AddPersistence</c>, so every context registered after it, the
    /// platform's and each module's, joins the bus's transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">A <see cref="FrappeDbContext"/> is already registered.</exception>
    public static TBuilder AddMessaging<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (
            builder.Services.FirstOrDefault(static service =>
                service.ServiceType.IsAssignableTo(typeof(FrappeDbContext))
            ) is
            { } registered
        )
        {
            throw new InvalidOperationException(
                $"{registered.ServiceType.Name} is registered before AddMessaging, so it would write outside the "
                    + "bus's outbox transaction. Call AddMessaging before AddPersistence and before any module "
                    + "registers its context; AddFrappePlatform does this in the right order."
            );
        }

        builder.AddHostRole();
        builder.Services.AddSingleton<IDbContextRegistrar>(new WolverineDbContextRegistrar());
        KernelMessageIdentityNaming.InstallOnce();
        bool development = builder.Environment.IsDevelopment();
        builder.Services.AddWolverine(
            ExtensionDiscovery.ManualOnly,
            options => Configure(options, development)
        );
        builder
            .Services.AddOptionsWithValidateOnStart<NatsOptions, NatsOptionsValidator>()
            .BindConfiguration(NatsOptions.SectionName);
        builder.Services.AddSingleton<IWolverineExtension, ModuleHandlerDiscovery>();
        builder.Services.AddSingleton<IWolverineExtension, NatsEvents>();
        // After AddWolverine, so they start once the bus has.
        builder.Services.AddHostedService<MessageStoreCheck>();
        builder.Services.AddHostedService<NatsTopologyCheck>();
        builder
            .Services.AddHealthChecks()
            .AddDependency<NatsHealthCheck>("nats", Criticality.Critical);
        builder.Services.AddSingleton<ErrorOrOutcome>();
        builder.Services.AddScoped<IDispatcher, WolverineDispatcher>();
        builder.Services.AddScoped<IEventPublisher, WolverineEventPublisher>();
        builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
        AddTelemetry(builder.Services);
        return builder;
    }

    private static void Configure(WolverineOptions options, bool development)
    {
        options.ServiceName = ServiceName;
        options.UseRuntimeCompilation();
        // TODO: the container PR switches to TypeLoadMode.Static with pre-generated handlers.
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
        options.ServiceLocationPolicy = ServiceLocationPolicy.NotAllowed;

        // Solo clears the node table at startup, so it is safe only with one process: development and tests.
        options.Durability.Mode = development ? DurabilityMode.Solo : DurabilityMode.Balanced;
        // Integration events travel through NATS JetStream (NatsEvents); commands run on durable local queues.
        options.PersistMessagesWithPostgresqlUsingDataSourceFromContainer(schemaName: Schema);
        // The app role cannot run DDL: the store's tables come from the owner-run migration.
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;

        options.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Eager);
        // No eager idempotency claim: an inline invoke has no inbox row to claim, and durable local queues
        // already deduplicate through the inbox.
        options.Policies.AutoApplyTransactions();

        options.Policies.UseDurableLocalQueues();
        options.Policies.UseDurableInboxOnAllListeners();
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        // Each in-flight handler holds a pooled connection inside its eager transaction; with the queues of one
        // process at this parallelism plus HTTP concurrency, the pool (PersistenceExtensions) stays the bound;
        // ModuleHandlerDiscovery fails the boot when the queues alone could exceed it.
        options.Policies.AllLocalQueues(queue =>
            queue.MaximumParallelMessages(LocalQueueParallelism)
        );

        options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
        options.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
        options.Durability.DeadLetterQueueExpirationEnabled = true;
        options.Durability.DeadLetterQueueExpiration = 30.Days();
        // The inbox remembers a handled envelope id at least as long as JetStream drops a repeated publish.
        options.Durability.KeepAfterMessageHandling = NatsTopology.DuplicateWindow;

        options.Policies.MessageSuccessLogLevel(LogLevel.Debug);
        options
            .Policies.OnException(IsTransient)
            .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds())
            .Then.ScheduleRetry(10.Seconds(), 1.Minutes(), 5.Minutes())
            .Then.MoveToErrorQueue();
        // Anything else is a bug: retrying would only hide it.
        options.Policies.OnAnyException().MoveToErrorQueue();

        // After AutoApplyTransactions, so the tenant is set before the transaction begins.
        options.Policies.Add<TenantPolicy>();
        options.Policies.Add<ErrorOrReturnPolicy>();
        // TODO: the security PR adds a chain policy that checks TCommand.Policy / TQuery.Policy before the
        // handler; it belongs on the chain because enqueued, scheduled and NATS-delivered messages never pass
        // through IDispatcher.
        options.Discovery.DisableConventionalDiscovery();

        // TODO: later PRs add events.event_log, domain-event scraping
        // (PublishDomainEventsFromEntityFrameworkCore), circuit breakers, a dead-letter replay CLI and the
        // Quartz outbox outside handlers (IDbContextOutbox).
    }

    // EF Core and callers wrap the driver's exception, so the whole inner chain is searched.
    private static bool IsTransient(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (IsTransientItself(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientItself(Exception exception) =>
        exception
            is PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure
                        or PostgresErrorCodes.DeadlockDetected
                }
                or NpgsqlException { IsTransient: true }
                or TimeoutException;

    private static void AddTelemetry(IServiceCollection services) =>
        // Wolverine's sources and meters; Frappe.Platform adds the resource, the exporter and the baseline.
        // Wolverine's meter already gauges the inbox and outbox size; inbox depth and oldest age per local queue
        // and the oldest outbox row need timestamps the 6.56 store tables lack (alerts PR).
        services
            .AddOpenTelemetry()
            .WithTracing(static tracing => tracing.AddSource("Wolverine"))
            .WithMetrics(static metrics =>
                metrics
                    .AddMeter("Wolverine*", ErrorOrOutcome.MeterName)
                    .AddView(static instrument =>
                        instrument.Meter.Name.StartsWith("Wolverine", StringComparison.Ordinal)
                            ? new MetricStreamConfiguration { TagKeys = _wolverineMetricTags }
                            : null
                    )
            );
}
