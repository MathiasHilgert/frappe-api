using Frappe.IntegrationEvents;
using Frappe.Messaging.Abstractions;
using Frappe.Notifications.Application.Messages;
using Frappe.Notifications.Application.Triggers;

namespace Frappe.Notifications.Infrastructure.Messaging;

/// <summary>
/// The before-claim step of a producer's consumer (S2a): the producer plug-in's mapper turns the fact into notifications and each runs the
/// dispatch pipeline, with the id of the message that carried the fact as its source message id, so a redelivery sends nothing again
/// (S14). Two notifications of one kind to one recipient over one channel in one fact are one notification. Every produced item is
/// checked before the first is sent, so one bad item sends nothing. A kind the mapper names that this host does not have, or a payload that is
/// not the kind's, throws like in the explicit path.
/// </summary>
internal sealed class DispatchProducedNotifications<TEvent>(
    INotificationProducer<TEvent> producer, CurrentMessage current, CatalogDispatch dispatch) : IBeforeClaimHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public async Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var sourceMessageId = current.Id
            ?? throw new InvalidOperationException($"A {typeof(TEvent).Name} was handled outside a consumer: it has no message id to send under.");
        var checkedItems = new List<(NotificationKind Kind, object Payload)>();
        foreach (var produced in producer.Map(integrationEvent))
        {
            var kind = dispatch.Require(sourceMessageId, produced.Kind);
            if (!kind.PayloadType.IsInstanceOfType(produced.Payload))
            {
                throw new InvalidOperationException(
                    $"The producer of {typeof(TEvent).Name} gave kind '{kind.Name}' a payload that is not a {kind.PayloadType.Name}.");
            }

            var valid = dispatch.Validate(kind, produced.Payload);
            if (!valid.IsSuccess)
            {
                var fields = string.Join(", ", (IEnumerable<string>)valid.Errors[0].Parameters["fields"]!);
                throw new InvalidOperationException(
                    $"The producer of {typeof(TEvent).Name} gave kind '{kind.Name}' a payload with invalid fields: {fields}.");
            }

            checkedItems.Add((kind, produced.Payload));
        }

        foreach (var (kind, payload) in checkedItems)
        {
            await dispatch.DispatchAsync(sourceMessageId, kind, payload, cancellationToken);
        }
    }
}
