namespace Frappe.Aggregates;

/// <summary>
/// Runs once the unit of work that raised the domain event committed, never when it rolled back: the place for effects
/// that must not happen before the data is visible, such as invalidating a cache (doing it earlier lets a reader cache
/// the old data again). Best effort: a failure is logged and the command still succeeds.
/// </summary>
#pragma warning disable CA1711 // "Handler" names the port's role; this is not a .NET event delegate.
public interface IAfterCommitDomainEventHandler<in TEvent>
#pragma warning restore CA1711
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
