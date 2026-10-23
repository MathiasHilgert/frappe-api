using System.Text.Json;
using Frappe.Contracts.Abstractions.Realtime;

namespace Frappe.Application.Abstractions.Realtime;

/// <summary>
/// The concrete message that carries a notification to live clients. Wolverine routes only concrete types, so
/// handlers return <c>RealtimePush.From(notification)</c> as a cascading message: it goes through the outbox and
/// is pushed only after the transaction commits.
/// </summary>
public sealed record RealtimePush(Guid EventId, Guid BusinessId, string Topic, string PayloadJson)
{
    public static RealtimePush From(IRealtimeNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new(
            notification.EventId,
            notification.BusinessId,
            notification.Topic,
            JsonSerializer.Serialize(notification, notification.GetType(), JsonSerializerOptions.Web));
    }
}
