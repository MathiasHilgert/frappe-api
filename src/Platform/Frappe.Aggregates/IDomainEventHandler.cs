namespace Frappe.Aggregates;

/// <summary>
/// Reacts to a domain event inside the unit of work of the command that raised it, right before the unit saves:
/// whatever it stages (a read model, for example) is written by the same <c>SaveChanges</c> and commits or rolls back
/// together with the aggregate. It must not raise domain events of its own. Throwing fails the command.
/// </summary>
#pragma warning disable CA1711 // "Handler" names the port's role; this is not a .NET event delegate.
public interface IDomainEventHandler<in TEvent>
#pragma warning restore CA1711
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
