using Frappe.Messaging.Abstractions;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Modules;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Infrastructure;

/// <summary>
/// The services of the Notifications module in every host: its database under the platform's limits, with the outbox that stages
/// the module's integration events in the same transaction as its data, and the declarations of its relay and purge, which only
/// the Worker's transport materializes.
/// </summary>
public sealed class NotificationsModule : IModule
{
    /// <summary>The lower-case module name of its namespaces (<c>Frappe.Notifications.*</c>), also its connection string name.</summary>
    internal const string Name = "notifications";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddKeyedSingleton<IIntegrationEventTranslator, NotificationsEventTranslator>(Name);
        services.AddModuleDatabase<NotificationsDbContext>(
            Name, NotificationsDbContext.Schema, (provider, options) => options.UseOutbox(provider, Name));
        services.AddOutboxRelay<NotificationsDbContext>(Name);
        services.AddMessagePurge<NotificationsDbContext>(Name);
    }
}
