namespace Frappe.Contracts.Abstractions.Realtime;

/// <summary>
/// A message pushed to live clients over SSE. <see cref="Topic"/> follows the event catalog's
/// "module.entity.fact" naming: three lowercase tokens of letters, digits or underscores.
/// </summary>
public interface IRealtimeNotification
{
    Guid EventId { get; }
    Guid BusinessId { get; }
    string Topic { get; }
}
