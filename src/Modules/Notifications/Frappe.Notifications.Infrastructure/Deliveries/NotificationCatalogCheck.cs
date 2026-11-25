using Frappe.Notifications.Application.Kinds;
using Microsoft.Extensions.Hosting;

namespace Frappe.Notifications.Infrastructure.Deliveries;

/// <summary>Builds the catalog when the host starts, so a repeated or malformed kind name stops the host there and not at the first send.</summary>
internal sealed class NotificationCatalogCheck(NotificationCatalog catalog) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = catalog.Kinds;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
