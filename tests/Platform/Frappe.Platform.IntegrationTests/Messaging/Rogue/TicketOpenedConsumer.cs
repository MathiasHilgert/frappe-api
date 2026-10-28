using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;

namespace Frappe.Platform.IntegrationTests.Messaging.Rogue.Application;

// Consumes TicketOpened without listing it in SubscribedEvents.
public sealed class TicketOpenedConsumer : IIntegrationEventConsumer<TicketOpened>
{
    public Task<ErrorOr<Success>> Handle(
        TicketOpened integrationEvent,
        CancellationToken cancellationToken
    ) => Task.FromResult<ErrorOr<Success>>(Result.Success);
}
