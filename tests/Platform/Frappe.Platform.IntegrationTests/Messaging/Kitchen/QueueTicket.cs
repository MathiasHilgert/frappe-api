using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

// Enqueued; the note steers it as it steers OpenTicket.
[MessageIdentity("kitchen.queue_ticket")]
public sealed record QueueTicket(Guid TicketId, string Note) : ICommand<QueueTicket>
{
    public static Policy Policy { get; } = new("kitchen.ticket.queue");
}
