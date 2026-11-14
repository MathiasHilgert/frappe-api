using Frappe.Health;
using Frappe.Messaging.Inbox;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Messaging.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Frappe.Messaging.Hosting;

public static class MessagingRegistration
{
    /// <summary>
    /// One NATS connection per process, its JetStream context and the publisher the relays use, bound from
    /// <c>ConnectionStrings:Nats</c> (required) and <c>Messaging:Namespace</c> (optional): every stream and subject of the
    /// process follows one <see cref="JetStreamNaming"/>, the production names unless a namespace is given (used to
    /// isolate several topologies on one server, as tests do). A missing URL fails at startup naming the key.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">The connection name the server shows.</param>
    public static IServiceCollection AddJetStream(this IServiceCollection services, string clientName)
    {
        services.AddOptions<JetStreamOptions>()
            .BindConfiguration(JetStreamOptions.Section)
            .Configure<IConfiguration>((options, configuration) => options.Url = configuration.GetConnectionString(JetStreamOptions.ConnectionStringName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Url), $"Configuration 'ConnectionStrings:{JetStreamOptions.ConnectionStringName}' is required.")
            .ValidateOnStart();
        return services.AddJetStreamServices(clientName);
    }

    /// <summary>The same as <see cref="AddJetStream(IServiceCollection, string)"/>, with the settings given in code.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="url">The NATS server URL.</param>
    /// <param name="clientName">The connection name the server shows.</param>
    /// <param name="namespace">An optional prefix for every stream and subject; null or empty keeps the production names.</param>
    public static IServiceCollection AddJetStream(this IServiceCollection services, string url, string clientName, string? @namespace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        services.AddOptions<JetStreamOptions>().Configure(options =>
        {
            options.Url = url;
            options.Namespace = @namespace;
        });
        return services.AddJetStreamServices(clientName);
    }

    private static IServiceCollection AddJetStreamServices(this IServiceCollection services, string clientName)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<JetStreamOptions>>().Value.Namespace is { Length: > 0 } prefix
            ? new JetStreamNaming(prefix)
            : JetStreamNaming.Default);
        services.TryAddSingleton(provider => new NatsConnection(new NatsOpts
        {
            Url = provider.GetRequiredService<IOptions<JetStreamOptions>>().Value.Url!,
            Name = clientName,
        }));
        services.TryAddSingleton<INatsJSContext>(provider => new NatsJSContext(provider.GetRequiredService<NatsConnection>()));
        services.AddCriticalDependency("nats", provider => new NatsHealthCheck(provider.GetRequiredService<NatsConnection>()));
        services.TryAddSingleton<IMessagePublisher, JetStreamPublisher>();
        return services;
    }

    /// <summary>
    /// Consumes one integration event type from a producer module's stream, through an inbox in
    /// <typeparamref name="TContext"/>'s schema. <paramref name="consumerName"/> is the durable name,
    /// <c>&lt;module&gt;-&lt;purpose&gt;</c>, and is forever: renaming it replays history into the inbox. Its options are
    /// named after it and bound from <paramref name="section"/> (<see cref="JetStreamConsumerOptions.Section"/> by
    /// default), validated at startup.
    /// </summary>
    public static IServiceCollection AddIntegrationEventConsumer<TContext, TEvent>(
        this IServiceCollection services,
        string producerModule,
        string consumerName,
        string section = JetStreamConsumerOptions.Section)
        where TContext : DbContext
        where TEvent : Frappe.IntegrationEvents.IIntegrationEvent
    {
        // Fail at registration, not with a TypeInitializationException the first time a message arrives.
        if (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Frappe.IntegrationEvents.IntegrationEventAttribute>(typeof(TEvent)) is null)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} has no [IntegrationEvent(\"<type>\")] attribute and cannot be consumed.");
        }

        services.AddOptions<JetStreamConsumerOptions>(consumerName)
            .BindConfiguration(section)
            .Configure<IConfiguration>((options, configuration) =>
                options.Backoff = ConfiguredList(configuration, section, nameof(JetStreamConsumerOptions.Backoff)) ?? options.Backoff)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JetStreamConsumerOptions>>(new JetStreamConsumerOptionsValidator(consumerName, section));
        services.AddOptions<ConsumerStepOptions>().Configure(options => options.Consumers.Add(new ConsumerDeclaration(consumerName, typeof(TEvent))));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider => new JetStreamConsumer<TContext, TEvent>(
            provider.GetRequiredService<INatsJSContext>(),
            new InboxConsumer<TContext, TEvent>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                consumerName,
                provider.GetRequiredService<ILogger<InboxConsumer<TContext, TEvent>>>(),
                provider.GetRequiredService<TimeProvider>()),
            producerModule,
            provider.GetRequiredService<IOptionsMonitor<JetStreamConsumerOptions>>().Get(consumerName),
            provider.GetRequiredService<ILogger<JetStreamConsumer<TContext, TEvent>>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<JetStreamNaming>()));
        return services;
    }

    /// <summary>
    /// Publishes a module's outbox continuously from this host, into the module's own stream. Each module keeps its
    /// own options, named after it and bound from <paramref name="section"/> (<see cref="OutboxRelayOptions.Section"/>
    /// by default), validated at startup.
    /// </summary>
    public static IServiceCollection AddOutboxRelay<TContext>(this IServiceCollection services, string module, string section = OutboxRelayOptions.Section)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        services.AddSingleton(new ProducerModule(module));
        if (!services.Any(d => d.ImplementationType == typeof(JetStreamTopologyService)))
        {
            services.AddHostedService<JetStreamTopologyService>();
        }

        services.AddOptions<OutboxRelayOptions>(module)
            .BindConfiguration(section)
            .Configure<IConfiguration>((options, configuration) =>
                options.RetryBackoff = ConfiguredList(configuration, section, nameof(OutboxRelayOptions.RetryBackoff)) ?? options.RetryBackoff)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OutboxRelayOptions>>(new OutboxRelayOptionsValidator(module, section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new OutboxRelay<TContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IMessagePublisher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(module),
            provider.GetRequiredService<ILogger<OutboxRelay<TContext>>>()));
        services.AddHostedService(provider => new OutboxRelayService<TContext>(
            provider.GetRequiredService<OutboxRelay<TContext>>(),
            provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(module),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<OutboxRelayService<TContext>>>()));
        return services;
    }

    /// <summary>
    /// Makes a <see cref="MessagePurger{TContext}"/> for <paramref name="module"/>'s outbox and inbox available to this
    /// host; the module's scheduled job runs it. Each module keeps its own options, named after it and bound from
    /// <paramref name="section"/> (<see cref="MessageRetentionOptions.Section"/> by default), validated at startup so a
    /// retention that would delete what is still needed fails naming the key.
    /// </summary>
    public static IServiceCollection AddMessagePurge<TContext>(this IServiceCollection services, string module, string section = MessageRetentionOptions.Section)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        services.AddOptions<MessageRetentionOptions>(module).BindConfiguration(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MessageRetentionOptions>>(new MessageRetentionOptionsValidator(module, section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<MessagingMetrics>();
        services.AddSingleton(provider => new MessagePurger<TContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            module,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptionsMonitor<MessageRetentionOptions>>().Get(module),
            provider.GetRequiredService<MessagingMetrics>(),
            provider.GetRequiredService<ILogger<MessagePurger<TContext>>>()));
        return services;
    }

    /// <summary>
    /// The list configured under <paramref name="section"/>:<paramref name="key"/>, or <see langword="null"/> when
    /// none is. The binder appends configured items to a list property's default instead of replacing it, so a list
    /// with a default is read again here and assigned whole.
    /// </summary>
    private static TimeSpan[]? ConfiguredList(IConfiguration configuration, string section, string key)
    {
        var list = configuration.GetSection(section).GetSection(key);
        return list.GetChildren().Any() ? list.Get<TimeSpan[]>() : null;
    }
}

/// <summary>
/// Runs the relay for the life of the host: a full batch means there may be more, so it goes again at once;
/// otherwise it waits <see cref="OutboxRelayOptions.PollInterval"/>. An unexpected failure (for example the database
/// being unreachable) is logged and retried with growing pauses; it never stops the host.
/// </summary>
internal sealed partial class OutboxRelayService<TContext>(
    OutboxRelay<TContext> relay,
    OutboxRelayOptions options,
    TimeProvider time,
    ILogger<OutboxRelayService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan pause;
            try
            {
                var published = await relay.PublishPendingAsync(stoppingToken);
                failures = 0;
                pause = published >= options.BatchSize ? TimeSpan.Zero : options.PollInterval;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                LogPassFailed(logger, exception, typeof(TContext).Name, failures);
                pause = options.RetryBackoff[Math.Min(failures - 1, options.RetryBackoff.Count - 1)];
            }

            if (pause > TimeSpan.Zero)
            {
                await Task.Delay(pause, time, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox relay for {Context} failed (failure {Failures}); retrying")]
    private static partial void LogPassFailed(ILogger logger, Exception exception, string context, int failures);
}

/// <summary>
/// Creates the JetStream streams this host produces into, plus the dead-letter stream. Runs in the background and
/// retries until NATS answers, so a broker that starts later never prevents the host from starting; until then the
/// relay treats publishing as an outage and keeps every message in the outbox.
/// </summary>
internal sealed partial class JetStreamTopologyService(
    INatsJSContext js,
    IEnumerable<ProducerModule> modules,
    TimeProvider time,
    ILogger<JetStreamTopologyService> logger,
    JetStreamNaming naming) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var module in modules)
                {
                    await JetStreamTopology.EnsureModuleStreamAsync(js, naming, module.Name, stoppingToken);
                }

                await JetStreamTopology.EnsureDeadLetterStreamAsync(js, naming, stoppingToken);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogTopologyFailed(logger, exception, delay);
                await Task.Delay(delay, time, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "JetStream streams could not be ensured; retrying in {Delay}")]
    private static partial void LogTopologyFailed(ILogger logger, Exception exception, TimeSpan delay);
}

/// <summary>A module whose integration events this host publishes.</summary>
internal sealed record ProducerModule(string Name);
