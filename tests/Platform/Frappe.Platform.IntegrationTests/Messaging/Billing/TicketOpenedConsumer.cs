using System.Collections.Concurrent;
using Frappe.Kernel.Messaging;
using Frappe.Kernel.Tenancy;
using Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Messaging.Billing.Application;

// Idempotent through its guarded write: a redelivered event finds the invoice and adds nothing.
public sealed class TicketOpenedConsumer(BillingDbContext db, ITenantContext tenant)
    : IIntegrationEventConsumer<TicketOpened>
{
    public const string Name = "billing";

    // Tickets whose event this consumer fails on, as a bug would.
    public static readonly ConcurrentDictionary<Guid, bool> Poisoned = new();

    public async Task<ErrorOr<Success>> Handle(
        TicketOpened integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (Poisoned.ContainsKey(integrationEvent.TicketId))
        {
            Runs.Record(integrationEvent.TicketId, Name);
            throw new InvalidOperationException(
                $"Billing hit a bug on {integrationEvent.TicketId}."
            );
        }

        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO billing.invoices (ticket_id, business_id) VALUES ({integrationEvent.TicketId}, {tenant.BusinessId}) ON CONFLICT (ticket_id) DO NOTHING",
            cancellationToken
        );
        // After the write, so a test that sees the run can wait for its commit.
        Runs.Record(integrationEvent.TicketId, Name);
        return Result.Success;
    }
}
