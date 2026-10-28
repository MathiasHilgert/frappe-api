using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

[GlobalMessage]
[MessageIdentity("kitchen.sweep_tickets")]
public sealed record SweepTickets(Guid RunId) : ICommand<SweepTickets>
{
    public static Policy Policy { get; } = new("kitchen.ticket.sweep");
}
