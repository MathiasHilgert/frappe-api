using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The email channel as the Worker lists it: the channel itself and the sender that takes its emails to the provider. The Api lists
/// neither, so it needs no provider key and never renders a mail.
/// </summary>
public sealed class EmailSenderWorkerModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddEmailChannel().AddEmailSender();
    }
}
