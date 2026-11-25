using Frappe.Identity.Infrastructure.Messaging;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>What <c>dotnet ef</c> builds the context with: the module's model and no database (the connection is never opened).</summary>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) => new(ModuleDatabase.Options<IdentityDbContext>(
        "Host=localhost;Database=frappe;Username=design",
        IdentityDbContext.Schema,
        options => options.UseOutbox(IdentityCoreServices.Name, new IdentityIntegrationEvents(), DesignTimeClock.Fixed)));
}
