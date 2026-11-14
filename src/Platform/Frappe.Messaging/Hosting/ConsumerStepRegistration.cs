using Frappe.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Hosting;

/// <summary>
/// The steps of one consumer, registered under its durable name so that several modules can consume the same event in
/// one host without running each other's steps. A consumer with steps of its own runs only those, and may have only
/// after-commit steps (no handler): it then just records the message in its inbox and runs them once it is committed.
/// A consumer with no step of its own keeps the event's single unnamed handler and unnamed after-commit steps.
/// A second handler or the same after-commit step twice fails at registration; a step whose consumer (the same name,
/// case-sensitive, consuming the same event through <see cref="MessagingRegistration.AddIntegrationEventConsumer{TContext, TEvent}"/>)
/// is not registered fails when the host starts.
/// </summary>
public static class ConsumerStepRegistration
{
    /// <summary>The handler <paramref name="consumerName"/> runs in its inbox transaction; at most one per consumer.</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services, string consumerName)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        if (Registered<IIntegrationEventHandler<TEvent>>(services, consumerName).FirstOrDefault() is { } existing)
        {
            throw new InvalidOperationException(
                $"Consumer '{consumerName}' already has handler {existing.Name} for {typeof(TEvent).Name}; {typeof(THandler).Name} cannot be added: a consumer has at most one handler.");
        }

        return services.AddStep<TEvent, THandler>(consumerName).AddKeyedScoped<IIntegrationEventHandler<TEvent>, THandler>(consumerName);
    }

    /// <summary>A step <paramref name="consumerName"/> runs after its inbox transaction committed (cache invalidation).</summary>
    public static IServiceCollection AddAfterCommitHandler<TEvent, THandler>(this IServiceCollection services, string consumerName)
        where TEvent : IIntegrationEvent
        where THandler : class, IAfterCommitHandler<TEvent>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        if (Registered<IAfterCommitHandler<TEvent>>(services, consumerName).Contains(typeof(THandler)))
        {
            throw new InvalidOperationException(
                $"Consumer '{consumerName}' already has after-commit step {typeof(THandler).Name} for {typeof(TEvent).Name}; registering it again would run it twice.");
        }

        return services.AddStep<TEvent, THandler>(consumerName).AddKeyedScoped<IAfterCommitHandler<TEvent>, THandler>(consumerName);
    }

    private static IEnumerable<Type> Registered<TService>(IServiceCollection services, string consumerName) =>
        services
            .Where(d => d.IsKeyedService && d.ServiceType == typeof(TService) && Equals(d.ServiceKey, consumerName))
            .Select(d => d.KeyedImplementationType ?? typeof(TService));

    private static IServiceCollection AddStep<TEvent, THandler>(this IServiceCollection services, string consumerName)
    {
        services.AddOptions<ConsumerStepOptions>()
            .Configure(options => options.Steps.Add(new ConsumerStep(consumerName, typeof(TEvent), typeof(THandler))))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ConsumerStepOptions>, ConsumerStepValidator>());
        return services;
    }
}

/// <summary>A step registered for a consumer by its durable name.</summary>
internal sealed record ConsumerStep(string Consumer, Type Event, Type Step);

/// <summary>A consumer registered by <see cref="MessagingRegistration.AddIntegrationEventConsumer{TContext, TEvent}"/>.</summary>
internal sealed record ConsumerDeclaration(string Consumer, Type Event);

/// <summary>The consumers and steps a host registered; validated at startup so every step has its consumer.</summary>
internal sealed class ConsumerStepOptions
{
    public List<ConsumerStep> Steps { get; } = [];

    public List<ConsumerDeclaration> Consumers { get; } = [];
}

internal sealed class ConsumerStepValidator : IValidateOptions<ConsumerStepOptions>
{
    public ValidateOptionsResult Validate(string? name, ConsumerStepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var declared = options.Consumers.ToHashSet();
        var failures = options.Steps
            .Where(step => !declared.Contains(new ConsumerDeclaration(step.Consumer, step.Event)))
            .Select(step =>
                $"Step {step.Step.Name} for {step.Event.Name} is registered for consumer '{step.Consumer}', but no consumer named '{step.Consumer}' consumes {step.Event.Name} (AddIntegrationEventConsumer), so it would never run.")
            .ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
