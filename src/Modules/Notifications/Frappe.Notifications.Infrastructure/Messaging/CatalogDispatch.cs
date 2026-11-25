using System.Text.Json;
using Frappe.Notifications.Application.Deliveries;
using Frappe.Notifications.Application.Messages;
using Frappe.Results;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// What every before-claim step of Notifications shares (S2): finding the kind a message names in this host's catalog and running the
/// dispatch pipeline for one request. A kind or payload this host does not know throws, so the message is retried and then dead-lettered
/// where an operator sees it: the producer validated both when it accepted the fact, so this is a deploy skew, never something to drop
/// silently. Nothing it throws carries the payload (S13).
/// </summary>
internal sealed class CatalogDispatch(
    NotificationCatalog catalog, NotificationDispatcher dispatcher, IEnumerable<INotificationPayloadCheck> checks)
{
    /// <summary>The kind named <paramref name="kindName"/>.</summary>
    /// <exception cref="InvalidOperationException">This host's catalog does not have it.</exception>
    public NotificationKind Require(Guid sourceMessageId, string kindName)
    {
        if (!catalog.TryGet(kindName, out var kind))
        {
            throw new InvalidOperationException(
                $"Notification request {sourceMessageId} names kind '{kindName}', which this host's catalog does not have.");
        }

        return kind;
    }

    /// <summary>The payload of an explicit request as the kind's, or the error naming its fields.</summary>
    public Result<object> Read(NotificationKind kind, JsonElement payload) => NotificationPayloads.Read(kind, payload, checks);

    /// <summary>A produced payload checked as the explicit path checks its own.</summary>
    public Result<object> Validate(NotificationKind kind, object payload) => NotificationPayloads.Validate(kind, payload, checks);

    /// <summary>Dispatches one request; its source message id makes a redelivery send nothing again (S14).</summary>
    public Task DispatchAsync(Guid sourceMessageId, NotificationKind kind, object payload, CancellationToken cancellationToken) =>
        dispatcher.DispatchAsync(new DeliveryRequest(sourceMessageId, kind, payload), cancellationToken);
}
