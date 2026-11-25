using Frappe.Messaging.Storage.Outbox;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Frappe.Notifications.Infrastructure.Persistence;

/// <summary>What <c>dotnet ef</c> builds the context with: the module's model and no database (the connection is never opened).</summary>
internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(ModuleDatabase.Options<NotificationsDbContext>(
        "Host=localhost;Database=frappe;Username=design",
        NotificationsDbContext.Schema,
        options => options.UseOutbox(NotificationsCoreServices.Name, new NotificationsEventTranslator(), DesignTimeClock.Fixed)));
}
