using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Messages.Marketing;

/// <summary>
/// The marketing notification kinds as a host lists them: the Api needs them to check a request against the catalog, the Worker to render
/// and send them, so both list it.
/// </summary>
public sealed class MarketingMessagesModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMarketingNotifications();
    }
}
