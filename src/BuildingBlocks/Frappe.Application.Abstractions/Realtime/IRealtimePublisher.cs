namespace Frappe.Application.Abstractions.Realtime;

/// <summary>
/// Outbound port to live clients. Prefer returning a <see cref="RealtimePush"/> from a handler over calling this
/// directly: as a cascading message it is only pushed after the transaction commits.
/// </summary>
public interface IRealtimePublisher
{
    Task PublishAsync(RealtimePush push, CancellationToken cancellationToken);
}
