using Frappe.Application.Abstractions.Realtime;

namespace Frappe.Platform.Realtime;

/// <summary>Runs from the durable local queue, after the outbox committed the push.</summary>
public static class RealtimePushHandler
{
    public static Task Handle(RealtimePush push, IRealtimePublisher publisher, CancellationToken ct) =>
        publisher.PublishAsync(push, ct);
}
