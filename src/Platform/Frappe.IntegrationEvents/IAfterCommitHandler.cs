namespace Frappe.IntegrationEvents;

/// <summary>
/// Runs after the consumer's transaction committed: the place for effects that must not happen before the data is
/// visible, such as invalidating a cache (doing it earlier lets a reader cache the old data again). Best effort: a
/// failure is logged and the message still counts as handled.
/// </summary>
public interface IAfterCommitHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
