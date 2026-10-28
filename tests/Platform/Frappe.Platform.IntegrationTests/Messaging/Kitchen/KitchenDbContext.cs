using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;

public sealed class KitchenDbContext(DbContextOptions<KitchenDbContext> options)
    : FrappeDbContext(options, "kitchen");
