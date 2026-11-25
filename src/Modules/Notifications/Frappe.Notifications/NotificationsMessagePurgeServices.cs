using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications;

/// <summary>
/// Registers <see cref="NotificationsMessagePurge"/> as the message purge the nightly Notifications purge runs. Only the Worker composes it,
/// last, as the Worker registered it after its module list; <c>NotificationsCoreServices</c> declares the purger it resolves.
/// </summary>
internal sealed class NotificationsMessagePurgeServices : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Infrastructure.Retention.IMessagePurge, NotificationsMessagePurge>();
    }
}
