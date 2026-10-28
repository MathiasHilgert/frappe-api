using Frappe.Kernel.Messaging;
using Frappe.Kernel.Tenancy;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

// Writes the ticket and publishes TicketOpened, then fails as the note asks.
internal static class Tickets
{
    public static async Task<ErrorOr<Success>> OpenAsync(
        KitchenDbContext db,
        IEventPublisher events,
        ITenantContext tenant,
        Guid ticketId,
        string note,
        CancellationToken cancellationToken
    )
    {
        int run = Runs.Record(ticketId);
        if (string.Equals(note, "flaky", StringComparison.Ordinal) && run == 1)
        {
            throw new TimeoutException($"Ticket {ticketId} timed out.");
        }

        // A transient failure as EF Core reports it: wrapped in an exception that is not transient itself.
        if (string.Equals(note, "wrapped-flaky", StringComparison.Ordinal) && run == 1)
        {
            throw new InvalidOperationException(
                $"Ticket {ticketId} failed.",
                new TimeoutException($"Ticket {ticketId} timed out.")
            );
        }

        bool inTransaction = db.Database.CurrentTransaction is not null;
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO kitchen.tickets (id, business_id, note, in_transaction) VALUES ({ticketId}, {tenant.BusinessId}, {note}, {inTransaction})",
            cancellationToken
        );
        await events.PublishAsync(new TicketOpened(ticketId), cancellationToken);
        return note switch
        {
            "reject" => Error.Conflict("kitchen.ticket.rejected", "The ticket was rejected."),
            "bug" => throw new InvalidOperationException($"Ticket {ticketId} hit a bug."),
            _ => Result.Success,
        };
    }
}
