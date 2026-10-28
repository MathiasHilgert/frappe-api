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
        return bus.InvokeAsync<ErrorOr<TResult>>(command, Delivery(bus), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ErrorOr<TResult>> QueryAsync<TQuery, TResult>(
        IQuery<TQuery, TResult> query,
        CancellationToken cancellationToken
    )
        where TQuery : IQuery<TQuery, TResult>
    {
        return bus.InvokeAsync<ErrorOr<TResult>>(query, Delivery(bus), cancellationToken);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TCommand>
    {
        await bus.SendAsync(command, Delivery(bus));
    }

    /// <inheritdoc />
    public async Task ScheduleAsync<TCommand>(
        TCommand command,
        Instant at,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand<TCommand>
    {
        DeliveryOptions delivery = Delivery(bus);
        delivery.ScheduledTime = at.ToDateTimeOffset();
        await bus.SendAsync(command, delivery);
    }

    // Stamped on each send, never on the bus: the bus lives as long as its scope, which may send for
    // several tenants in turn. The open tenant scope is the current one (inside a handler, TenantMiddleware
    // opened it from the envelope); without one, the bus's own tenant, if any, is inherited.
    internal static DeliveryOptions Delivery(IMessageBus bus) =>
        new() { TenantId = TenantScope.Current?.BusinessId.ToString() ?? bus.TenantId };
}
