using System.Reflection;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Observability;

public static class HandlerRegistration
{
    /// <summary>
    /// The decorators of each port, innermost first. Instrumentation is outermost, so a command's span and duration
    /// include its commit and an exception thrown by the commit is logged like any other.
    /// </summary>
    private static readonly (Type Port, Type[] Decorators)[] Ports =
    [
        (typeof(ICommandHandler<,>), [typeof(TransactionalCommandHandler<,>), typeof(InstrumentedCommandHandler<,>)]),
        (typeof(IQueryHandler<,>), [typeof(InstrumentedQueryHandler<,>)]),
    ];

    /// <summary>
    /// Registers every command and query handler found in <paramref name="assemblies"/> as a scoped service,
    /// already wrapped by the platform pipeline: every command runs in its module's unit of work
    /// (<see cref="TransactionalCommandHandler{TCommand, TResult}"/>), and every handler is instrumented. Callers resolve the port interface, never the class.
    /// Registering the same assembly twice is harmless; a second handler for the same port is a startup error,
    /// because every command and query is handled by exactly one handler.
    /// </summary>
    public static IServiceCollection AddHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<HandlerMetrics>();
        var registry = RegistryOf(services);

        var handlers = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var implementation in handlers)
        {
            foreach (var port in implementation.GetInterfaces().Where(i => i.IsGenericType))
            {
                var decorators = Ports.SingleOrDefault(p => p.Port == port.GetGenericTypeDefinition()).Decorators;
                if (decorators is null || !registry.Claim(port, implementation, services))
                {
                    continue;
                }

                // The constructors are resolved once here, not on every request.
                var factories = decorators
                    .Select(decorator => ActivatorUtilities.CreateFactory(decorator.MakeGenericType(port.GetGenericArguments()), [port]))
                    .ToArray();
                services.TryAddScoped(implementation);
                services.AddScoped(port, provider => factories.Aggregate(
                    provider.GetRequiredService(implementation),
                    (inner, createDecorator) => createDecorator(provider, [inner])));
            }
        }

        return services;
    }

    private static HandlerRegistry RegistryOf(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(HandlerRegistry))?.ImplementationInstance;
        if (existing is HandlerRegistry registry)
        {
            return registry;
        }

        registry = new HandlerRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>Remembers which implementation owns each port, so the one-handler-per-port rule is checked at startup.</summary>
    private sealed class HandlerRegistry
    {
        private readonly Dictionary<Type, Type> _owners = [];

        /// <summary>True when the port must be registered now; false when this implementation already owns it.</summary>
        public bool Claim(Type port, Type implementation, IServiceCollection services)
        {
            if (_owners.TryGetValue(port, out var owner))
            {
                return owner == implementation
                    ? false
                    : throw Conflict(port, owner, implementation);
            }

            var foreign = services.FirstOrDefault(d => d.ServiceType == port && !d.IsKeyedService);
            if (foreign is not null)
            {
                throw Conflict(port, foreign.ImplementationType, implementation);
            }

            _owners[port] = implementation;
            return true;
        }

        private static InvalidOperationException Conflict(Type port, Type? first, Type second) => new(
            $"'{RequestOf(port)}' has more than one handler: '{first?.Name ?? "a manual registration"}' and '{second.Name}'. "
            + "Every command and query must be handled by exactly one handler.");

        private static string RequestOf(Type port) => port.GetGenericArguments()[0].Name;
    }
}
