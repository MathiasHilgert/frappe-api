using Frappe.Kernel.Messaging;
using NodaTime;
using Wolverine;

namespace Frappe.Platform.Messaging;

/// <summary>
/// <see cref="IDispatcher"/> on the Wolverine bus. Public and constructor-injected so a handler gets the one
/// built from its own message context, which keeps every enqueue inside the handler's outbox transaction.
/// </summary>
/// <param name="bus">The message context of the current handler or scope.</param>
public sealed class WolverineDispatcher(IMessageContext bus) : IDispatcher
{
    /// <inheritdoc />
    public Task<ErrorOr<TResult>> InvokeAsync<TCommand, TResult>(
        ICommand<TCommand, TResult> command,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand<TCommand, TResult>
    {
        StampTenant(bus);
        return bus.InvokeAsync<ErrorOr<TResult>>(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ErrorOr<TResult>> QueryAsync<TQuery, TResult>(
        IQuery<TQuery, TResult> query,
        CancellationToken cancellationToken
    )
        where TQuery : IQuery<TQuery, TResult>
    {
        StampTenant(bus);
        return bus.InvokeAsync<ErrorOr<TResult>>(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TCommand>
    {
        StampTenant(bus);
        await bus.SendAsync(command);
    }

    /// <inheritdoc />
    public async Task ScheduleAsync<TCommand>(
        TCommand command,
        Instant at,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand<TCommand>
    {
        StampTenant(bus);
        await bus.SendAsync(command, new DeliveryOptions { ScheduledTime = at.ToDateTimeOffset() });
    }

    // Inside a handler the context already carries the envelope's tenant, which every message it sends
    // inherits; outside one (an HTTP request, a job) the open tenant scope supplies it.
    internal static void StampTenant(IMessageBus bus)
    {
        if (bus.TenantId is null && TenantScope.Current is { } tenant)
        {
            bus.TenantId = tenant.BusinessId.ToString();
        }
    }
}
