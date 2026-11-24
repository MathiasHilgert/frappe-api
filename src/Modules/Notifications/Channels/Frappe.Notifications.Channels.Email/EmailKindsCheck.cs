using Frappe.Notifications.Application.Kinds;
using Microsoft.Extensions.Hosting;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Composes the sample of every email kind in every language when the host starts, so a missing text or argument, or a link in a Security
/// kind, stops the host there and not at the first send (S7, S8).
/// </summary>
public sealed class EmailKindsCheck(NotificationCatalog catalog, EmailComposer composer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var kind in catalog.Kinds.Where(composer.CanCompose))
        {
            foreach (var language in NotificationLanguages.All)
            {
                _ = composer.Compose(kind, kind.Sample, language);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
