using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;

[MessageIdentity("kitchen.ticket.opened.v1")]
public sealed record TicketOpened(Guid TicketId) : IIntegrationEvent;
