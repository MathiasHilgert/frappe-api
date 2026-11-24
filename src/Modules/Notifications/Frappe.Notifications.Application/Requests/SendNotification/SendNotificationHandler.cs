using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Requests;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Notifications.Application.Requests.SendNotification;

/// <summary>
/// Accepts an explicit request to send a notification: the kind must be in the catalog (<c>notifications.kind.unknown</c>) and the
/// payload must be the kind's (<c>notifications.payload.invalid</c>, naming the fields). A request already accepted under the same
/// identity succeeds without staging anything, so an HTTP retry collapses. Accepting stages a request that raises the event the dispatch
/// consumes; the unit of work saves it, with the outbox row, in one transaction.
/// </summary>
internal sealed class SendNotificationHandler(NotificationCatalog catalog, ISendRequests requests, IClock clock)
    : ICommandHandler<SendNotification, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SendNotification command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!catalog.TryGet(command.Kind, out var kind))
        {
            return Error.InvalidValue("notifications.kind.unknown", ("kind", command.Kind));
        }

        var payload = NotificationPayloads.Read(kind, command.Payload);
        if (!payload.IsSuccess)
        {
            return Result.Failure<Unit>(payload.Errors);
        }

        if (!await requests.ExistsAsync(command.RequestId, cancellationToken))
        {
            requests.Stage(SendRequest.Accept(command.RequestId, kind.Name, command.Payload, clock.GetCurrentInstant()));
        }

        return Unit.Value;
    }
}
