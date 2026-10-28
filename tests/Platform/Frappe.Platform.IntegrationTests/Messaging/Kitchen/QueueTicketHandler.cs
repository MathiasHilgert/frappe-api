using Frappe.Kernel.Messaging;
using Frappe.Kernel.Tenancy;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

public sealed class QueueTicketHandler(
    KitchenDbContext db,
    IEventPublisher events,
    ITenantContext tenant
) : ICommandHandler<QueueTicket, Success>
{
    public Task<ErrorOr<Success>> Handle(
        QueueTicket command,
        CancellationToken cancellationToken
    ) => Tickets.OpenAsync(db, events, tenant, command.TicketId, command.Note, cancellationToken);
}
