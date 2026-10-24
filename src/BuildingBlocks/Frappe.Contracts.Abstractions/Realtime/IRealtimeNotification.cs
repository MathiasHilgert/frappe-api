namespace Frappe.Contracts.Abstractions.Realtime;

/// <summary>
/// A message pushed to live clients over SSE. <see cref="Topic"/> follows the event catalog's
/// "module.entity.fact" naming: three lowercase tokens of letters, digits or underscores.
/// </summary>
public interface IRealtimeNotification
{
    /// <summary>Unique per event, so a client that reconnects can drop what it already saw.</summary>
    Guid EventId { get; }

    /// <summary>The business whose live clients receive the event.</summary>
    Guid BusinessId { get; }

    /// <summary>What happened, e.g. <c>tables.table.opened</c>.</summary>
    string Topic { get; }
}
