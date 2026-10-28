namespace Frappe.Kernel.Messaging;

/// <summary>
/// Reacts to <typeparamref name="TEvent"/> in its own transaction; only the bus calls it, at least once.
/// </summary>
/// <typeparam name="TEvent">The integration event handled; listed in the module's subscribed events.</typeparam>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>Runs the reaction; it must be idempotent, because a redelivery runs it again.</summary>
    /// <param name="integrationEvent">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or every expected failure, which is logged and acknowledged, never retried.</returns>
    Task<ErrorOr<Success>> Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}
