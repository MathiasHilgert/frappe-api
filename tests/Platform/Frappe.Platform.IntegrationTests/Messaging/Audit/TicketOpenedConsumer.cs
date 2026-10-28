using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Messaging.Audit.Application;

// Appends one entry per delivery, so it shows how many times the event arrived.
public sealed class TicketOpenedConsumer(AuditDbContext db)
    : IIntegrationEventConsumer<TicketOpened>
{
    public const string Name = "audit";

    public async Task<ErrorOr<Success>> Handle(
        TicketOpened integrationEvent,
        CancellationToken cancellationToken
    )
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO audit.entries (ticket_id) VALUES ({integrationEvent.TicketId})",
            cancellationToken
        );
        // After the write, so a test that sees the run can wait for its commit.
        Runs.Record(integrationEvent.TicketId, Name);
        return Result.Success;
    }
}
