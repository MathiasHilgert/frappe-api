using Frappe.Messaging.Abstractions;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Contracts;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// The dispatch consumer's before-claim step (S2, S14): reads the requested kind and payload as the catalog knows them and runs the
/// dispatch pipeline, which calls the provider outside any transaction and stages the delivery rows. The claim of the message saves them
/// with the inbox record. Its source message id is the request's own id, so an HTTP retry that produced a second message sends nothing
/// again. A kind or payload this host does not know throws, so the message is retried and then dead-lettered where an operator sees it:
/// the Api validated both when it accepted the request, so this is a deploy skew, never something to drop silently.
/// </summary>
internal sealed class DispatchNotificationRequested(CatalogDispatch dispatch)
    : IBeforeClaimHandler<NotificationRequested>
{
    public async Task HandleAsync(NotificationRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var kind = dispatch.Require(integrationEvent.RequestId, integrationEvent.Kind);
        var payload = NotificationPayloads.Read(kind, integrationEvent.Payload);
        if (!payload.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Notification request {integrationEvent.RequestId} of kind '{kind.Name}' carries a payload that is not the kind's.");
        }

        await dispatch.DispatchAsync(integrationEvent.RequestId, kind, payload.Value, cancellationToken);
    }
}
