namespace Frappe.IntegrationEvents;

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
