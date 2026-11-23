using System.Reflection;
using Frappe.Health;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Inbox;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Messaging.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Frappe.Messaging.Hosting;

/// <summary>Registers messaging in a host: the outbox relay and JetStream topology for a producer, and the consumers a host runs.</summary>
public static class MessagingRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Makes a <see cref="MessagePurger{TContext}"/> for <paramref name="module"/>'s outbox and inbox available to this
        /// host; the module's scheduled job runs it. Each module keeps its own options, named after it and bound from
        /// <paramref name="section"/> (<see cref="MessageRetentionOptions.Section"/> by default), validated at startup so a
        /// retention that would delete what is still needed fails naming the key.
        /// </summary>
        public IServiceCollection AddMessagePurge<TContext>(string module, string section = MessageRetentionOptions.Section)
            where TContext : DbContext
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module);
            services.AddOptions<MessageRetentionOptions>(module)
                .BindConfiguration(section)
                .Configure(options => options.ConfigurationSection = section)
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessageRetentionOptions>, MessageRetentionOptionsValidator>());
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
        /// One NATS connection per process, its JetStream context and the publisher the relays use, bound from
        /// <c>ConnectionStrings:Nats</c> (required) and <c>Messaging:Namespace</c> (optional): every stream and subject of the
        /// process follows one <see cref="JetStreamNaming"/>, the production names unless a namespace is given (used to
        /// isolate several topologies on one server, as tests do). A missing URL fails at startup naming the key.
        /// </summary>
        /// <param name="clientName">The connection name the server shows.</param>
        public IServiceCollection AddJetStream(string clientName)
        {
            services.AddOptions<JetStreamOptions>()
                .BindConfiguration(JetStreamOptions.Section)
                .Configure<IConfiguration>(
                    (options, configuration) => options.Url = configuration.GetConnectionString(JetStreamOptions.ConnectionStringName))
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JetStreamOptions>, JetStreamOptionsValidator>());
            return services.AddJetStreamServices(clientName);
        }

        /// <summary>The same as <see cref="AddJetStream(IServiceCollection, string)"/>, with the settings given in code.</summary>
        /// <param name="url">The NATS server URL.</param>
        /// <param name="clientName">The connection name the server shows.</param>
        /// <param name="namespace">An optional prefix for every stream and subject; null or empty keeps the production names.</param>
        public IServiceCollection AddJetStream(string url, string clientName, string? @namespace = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            services.AddOptions<JetStreamOptions>().Configure(options =>
            {
                options.Url = url;
                options.Namespace = @namespace;
            });
            return services.AddJetStreamServices(clientName);
        }

        private IServiceCollection AddJetStreamServices(string clientName)
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
        /// <c>&lt;module&gt;-&lt;purpose&gt;</c>, and is forever: renaming it redelivers the retained history into the inbox. Its options are
        /// named after it and bound from <paramref name="section"/> (<see cref="JetStreamConsumerOptions.Section"/> by
        /// default), validated at startup.
        /// </summary>
        public IServiceCollection AddIntegrationEventConsumer<TContext, TEvent>(string producerModule,
            string consumerName,
            string section = JetStreamConsumerOptions.Section)
            where TContext : DbContext
            where TEvent : Frappe.IntegrationEvents.IIntegrationEvent
        {
            // Fail at registration, not with a TypeInitializationException the first time a message arrives.
            if (typeof(TEvent).GetCustomAttribute<IntegrationEventAttribute>() is null)
            {
                throw new InvalidOperationException($"{typeof(TEvent).Name} has no [IntegrationEvent(\"<type>\")] attribute and cannot be consumed.");
            }

            services.AddOptions<JetStreamConsumerOptions>(consumerName)
                .BindConfiguration(section)
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.ConfigurationSection = section;
                    options.Backoff = ConfiguredList(configuration, section, nameof(JetStreamConsumerOptions.Backoff)) ?? options.Backoff;
                })
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JetStreamConsumerOptions>, JetStreamConsumerOptionsValidator>());
            services.AddOptions<ConsumerStepOptions>()
                .Configure(options => options.Consumers.Add(new ConsumerDeclaration(consumerName, typeof(TEvent))));
            services.TryAddSingleton(TimeProvider.System);
            services.AddMetrics();
            services.TryAddSingleton<MessagingMetrics>();
            services.AddHostedService(provider => new JetStreamConsumer<TContext, TEvent>(
                provider.GetRequiredService<INatsJSContext>(),
                new InboxConsumer<TContext, TEvent>(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    consumerName,
                    provider.GetRequiredService<ILogger<InboxConsumer<TContext, TEvent>>>(),
                    provider.GetRequiredService<TimeProvider>()),
                producerModule,
                provider.GetRequiredService<IOptionsMonitor<JetStreamConsumerOptions>>().Get(consumerName),
                provider.GetRequiredService<MessagingMetrics>(),
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
        public IServiceCollection AddOutboxRelay<TContext>(string module, string section = OutboxRelayOptions.Section)
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
                {
                    options.ConfigurationSection = section;
                    options.RetryBackoff = ConfiguredList(configuration, section, nameof(OutboxRelayOptions.RetryBackoff)) ?? options.RetryBackoff;
                })
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OutboxRelayOptions>, OutboxRelayOptionsValidator>());
            services.TryAddSingleton(TimeProvider.System);
            services.AddMetrics();
            services.TryAddSingleton<MessagingMetrics>();
            services.AddSingleton(provider => new OutboxRelay<TContext>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IMessagePublisher>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(module),
                module,
                provider.GetRequiredService<MessagingMetrics>(),
                provider.GetRequiredService<ILogger<OutboxRelay<TContext>>>()));
            services.AddHostedService(provider => new OutboxRelayService<TContext>(
                provider.GetRequiredService<OutboxRelay<TContext>>(),
                provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(module),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<OutboxRelayService<TContext>>>()));
            return services;
        }
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
