using System.Collections.Concurrent;
using System.Reflection;
using Frappe.Aggregates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Persistence;

/// <summary>
/// Hands a domain event to the handlers registered for its runtime type: the <see cref="IDomainEventHandler{TEvent}"/>s
/// inside the unit of work, the <see cref="IAfterCommitDomainEventHandler{TEvent}"/>s once it committed. The generic
/// method for each event type is bound once and cached.
/// </summary>
internal static partial class DomainEventDispatch
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> InUnit = new();

    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, ILogger, CancellationToken, Task>> AfterCommit = new();

    /// <summary>Runs every in-unit handler of the event in turn; the first one that throws fails the unit.</summary>
    public static Task HandleAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        InUnit.GetOrAdd(domainEvent.GetType(), type => Bind<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>(nameof(HandleInUnitAsync), type))(
            services, domainEvent, cancellationToken);

    /// <summary>
    /// Runs every after-commit handler of the event; a failure is logged and the next one still runs. Handlers the
    /// container cannot build are logged too, and then none runs: the commit stands either way.
    /// </summary>
    public static Task AfterCommitAsync(IServiceProvider services, IDomainEvent domainEvent, ILogger logger, CancellationToken cancellationToken) =>
        AfterCommit.GetOrAdd(domainEvent.GetType(), type => Bind<Func<IServiceProvider, IDomainEvent, ILogger, CancellationToken, Task>>(nameof(HandleAfterCommitAsync), type))(
            services, domainEvent, logger, cancellationToken);

    private static TDelegate Bind<TDelegate>(string method, Type eventType)
        where TDelegate : Delegate =>
#pragma warning disable S3011 // Binds this class's own private generic handlers to a runtime event type; nothing outside is opened.
        typeof(DomainEventDispatch).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
#pragma warning restore S3011
            .MakeGenericMethod(eventType)
            .CreateDelegate<TDelegate>();

    private static async Task HandleInUnitAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }

    private static async Task HandleAfterCommitAsync<TEvent>(
        IServiceProvider services, IDomainEvent domainEvent, ILogger logger, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        // The container builds every handler at once, so one that cannot be built leaves none to run; it is logged
        // like a failed step, because the unit already committed and its command must still succeed.
        IAfterCommitDomainEventHandler<TEvent>[] handlers;
        try
        {
            handlers = [.. services.GetServices<IAfterCommitDomainEventHandler<TEvent>>()];
        }
        catch (Exception exception)
        {
            LogAfterCommitUnresolved(logger, exception, typeof(TEvent).Name);
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
            }
            catch (Exception exception)
            {
                LogAfterCommitFailed(logger, exception, typeof(TEvent).Name, handler.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A unit of work committed {Event} but after-commit step {Step} failed")]
    private static partial void LogAfterCommitFailed(ILogger logger, Exception exception, string @event, string step);

    [LoggerMessage(Level = LogLevel.Error, Message = "A unit of work committed {Event} but its after-commit steps could not be built")]
    private static partial void LogAfterCommitUnresolved(ILogger logger, Exception exception, string @event);
}
