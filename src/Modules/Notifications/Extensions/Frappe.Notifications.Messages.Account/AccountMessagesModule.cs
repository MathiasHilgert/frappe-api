using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Messages.Account;

/// <summary>
/// The account notification kinds as a host lists them: the Api needs them to check a request against the catalog, the Worker to render and
/// send them, so both list it.
/// </summary>
public sealed class AccountMessagesModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAccountNotifications();
    }
}
