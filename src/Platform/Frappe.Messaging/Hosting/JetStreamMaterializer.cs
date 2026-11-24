using Frappe.IntegrationEvents;
using Frappe.Messaging.Abstractions;
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

namespace Frappe.Messaging.Hosting;

/// <summary>
/// Turns what modules declared in <see cref="MessagingDeclarations"/> into the Worker's relays, consumers, JetStream topology and
/// purgers, in the service collection <c>AddJetStream</c> was called on.
/// A consumer has its own steps, registered under its durable name so that several modules can consume the same event in one host
/// without running each other's steps: it runs only those, and may have only after-commit steps (no handler), then it just records
/// the message in its inbox and runs them once it is committed. It may also have one before-claim step. A second handler or the
/// same after-commit step twice fails when materialized; a step whose consumer (the same name, case-sensitive, consuming the same
/// event) is not declared fails when the host starts.
/// </summary>
internal sealed class JetStreamMaterializer(IServiceCollection services) : IMessagingDeclarationVisitor
{
    public void Purge<TContext>(MessagePurgeDeclaration<TContext> declaration)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var module = declaration.Module;
        var section = declaration.Section ?? MessageRetentionOptions.Section;
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
    }

    public void Consumer<TContext, TEvent>(IntegrationEventConsumerDeclaration<TContext, TEvent> declaration)
        where TContext : DbContext
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var (producerModule, consumerName, _) = declaration;
        var section = declaration.Section ?? JetStreamConsumerOptions.Section;
        services.AddOptions<JetStreamConsumerOptions>(consumerName)
            .BindConfiguration(section)
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ConfigurationSection = section;
                options.Backoff = MessagingRegistration.ConfiguredList(configuration, section, nameof(JetStreamConsumerOptions.Backoff))
                    ?? options.Backoff;
            })
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JetStreamConsumerOptions>, JetStreamConsumerOptionsValidator>());
        services.AddOptions<ConsumerStepOptions>()
            .Configure(options => options.Consumers.Add(new ConsumerDeclaration(consumerName, typeof(TEvent))));
        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<MessagingMetrics>();
        services.AddHostedService(provider => new JetStreamConsumer<TContext, TEvent>(
            provider.GetRequiredService<NATS.Client.JetStream.INatsJSContext>(),
            new InboxConsumer<TContext, TEvent>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                consumerName,
                provider.GetRequiredService<ILogger<InboxConsumer<TContext, TEvent>>>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<MessagingMetrics>()),
            producerModule,
            provider.GetRequiredService<IOptionsMonitor<JetStreamConsumerOptions>>().Get(consumerName),
            provider.GetRequiredService<MessagingMetrics>(),
            provider.GetRequiredService<ILogger<JetStreamConsumer<TContext, TEvent>>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<JetStreamNaming>()));
    }

    public void Relay<TContext>(OutboxRelayDeclaration<TContext> declaration)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var module = declaration.Module;
        var section = declaration.Section ?? OutboxRelayOptions.Section;
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
                options.RetryBackoff = MessagingRegistration.ConfiguredList(configuration, section, nameof(OutboxRelayOptions.RetryBackoff))
                    ?? options.RetryBackoff;
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
    }

    public void BeforeClaim<TEvent, THandler>(BeforeClaimHandlerDeclaration<TEvent, THandler> declaration)
        where TEvent : IIntegrationEvent
        where THandler : class, IBeforeClaimHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var consumerName = declaration.ConsumerName;
        if (Registered<IBeforeClaimHandler<TEvent>>(consumerName).FirstOrDefault() is { } existing)
        {
            throw new InvalidOperationException(
                $"Consumer '{consumerName}' already has before-claim step {existing.Name} for {typeof(TEvent).Name}; "
                + $"{typeof(THandler).Name} cannot be added: a consumer has at most one.");
        }

        AddStep<TEvent, THandler>(consumerName);
        services.AddKeyedScoped<IBeforeClaimHandler<TEvent>, THandler>(consumerName);
    }

    public void Handler<TEvent, THandler>(IntegrationEventHandlerDeclaration<TEvent, THandler> declaration)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var consumerName = declaration.ConsumerName;
        if (Registered<IIntegrationEventHandler<TEvent>>(consumerName).FirstOrDefault() is { } existing)
        {
            throw new InvalidOperationException(
                $"Consumer '{consumerName}' already has handler {existing.Name} for {typeof(TEvent).Name}; "
                + $"{typeof(THandler).Name} cannot be added: a consumer has at most one handler.");
        }

        AddStep<TEvent, THandler>(consumerName);
        services.AddKeyedScoped<IIntegrationEventHandler<TEvent>, THandler>(consumerName);
    }

    public void AfterCommit<TEvent, THandler>(AfterCommitHandlerDeclaration<TEvent, THandler> declaration)
        where TEvent : IIntegrationEvent
        where THandler : class, IAfterCommitHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var consumerName = declaration.ConsumerName;
        if (Registered<IAfterCommitHandler<TEvent>>(consumerName).Contains(typeof(THandler)))
        {
            throw new InvalidOperationException(
                $"Consumer '{consumerName}' already has after-commit step {typeof(THandler).Name} for {typeof(TEvent).Name}; "
                + "registering it again would run it twice.");
        }

        AddStep<TEvent, THandler>(consumerName);
        services.AddKeyedScoped<IAfterCommitHandler<TEvent>, THandler>(consumerName);
    }

    private void AddStep<TEvent, THandler>(string consumerName)
    {
        services.AddOptions<ConsumerStepOptions>()
            .Configure(options => options.Steps.Add(new ConsumerStep(consumerName, typeof(TEvent), typeof(THandler))))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ConsumerStepOptions>, ConsumerStepValidator>());
    }

    private IEnumerable<Type> Registered<TService>(string consumerName) =>
        services
            .Where(d => d.IsKeyedService && d.ServiceType == typeof(TService) && Equals(d.ServiceKey, consumerName))
            .Select(d => d.KeyedImplementationType ?? typeof(TService));
}
