using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options)
    : FrappeDbContext(options, "audit");
