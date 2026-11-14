namespace Frappe.IntegrationEvents;

/// <summary>
/// A fact a module publishes for other modules (and, later, other systems). It is part of the module's public
/// contract: its payload may only grow with optional fields; any other change is a new type with a new version.
/// </summary>
public interface IIntegrationEvent;

/// <summary>
/// The stable CloudEvents <c>type</c> of an integration event, for example <c>frappe.geo.holidays_synced.v1</c>:
/// <c>frappe.&lt;module&gt;.&lt;fact&gt;.v&lt;version&gt;</c>. It never changes once published.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}

/// <summary>
/// Inbound port of a module for an integration event published by another module (or by itself). It runs inside
/// the consumer's transaction: whatever it writes commits together with the inbox record, or not at all.
/// Throwing means "retry later".
/// </summary>
#pragma warning disable CA1711 // "Handler" names the port's role; this is not a .NET event delegate.
public interface IIntegrationEventHandler<in TEvent>
#pragma warning restore CA1711
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

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
