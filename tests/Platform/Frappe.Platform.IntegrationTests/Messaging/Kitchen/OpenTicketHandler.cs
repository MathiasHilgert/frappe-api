using Frappe.Kernel.Messaging;
using Frappe.Kernel.Tenancy;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

public sealed class OpenTicketHandler(
    KitchenDbContext db,
    IEventPublisher events,
    ITenantContext tenant
) : ICommandHandler<OpenTicket, Guid>
{
    public async Task<ErrorOr<Guid>> Handle(OpenTicket command, CancellationToken cancellationToken)
    {
        ErrorOr<Success> opened = await Tickets.OpenAsync(
            db,
            events,
            tenant,
            command.TicketId,
            command.Note,
            cancellationToken
        );
        return opened.IsError ? opened.Errors : command.TicketId;
    }
}
