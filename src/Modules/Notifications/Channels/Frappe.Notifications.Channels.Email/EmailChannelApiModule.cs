using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// The email channel as the Api lists it: what receives the provider's webhooks and the trial that refuses a payload no email can be made
/// of. The Api never renders or sends a mail, so it lists neither the channel nor a sender, and needs no provider key (the Worker's
/// <see cref="EmailChannelWorkerModule"/> does).
/// </summary>
public sealed class EmailChannelApiModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddResendWebhook().AddEmailPayloadCheck();
    }
}
