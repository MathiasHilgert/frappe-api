using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options)
    : FrappeDbContext(options, "billing");
