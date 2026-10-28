using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Rogue.Contracts;

// An event the bus would store without a stable name.
public sealed record UnnamedEvent(Guid Id) : IIntegrationEvent;
