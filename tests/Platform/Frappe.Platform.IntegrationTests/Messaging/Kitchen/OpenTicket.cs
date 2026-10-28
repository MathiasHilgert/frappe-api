using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

// The note steers the fixture: "reject" returns an error, "bug" throws, "flaky" times out on its first run.
[MessageIdentity("kitchen.open_ticket")]
public sealed record OpenTicket(Guid TicketId, string Note) : ICommand<OpenTicket, Guid>
{
    public static Policy Policy { get; } = new("kitchen.ticket.open");
}
