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

    public async Task<ErrorOr<Success>> Handle(
        TicketOpened integrationEvent,
        CancellationToken cancellationToken
    )
    {
        Runs.Record(integrationEvent.TicketId, Name);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO billing.invoices (ticket_id, business_id) VALUES ({integrationEvent.TicketId}, {tenant.BusinessId}) ON CONFLICT (ticket_id) DO NOTHING",
            cancellationToken
        );
        return Result.Success;
    }
}
