using Frappe.Notifications.Application.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Notifications.Channels.Email;

/// <summary>Registers the email channel in a host. The sender and the unsubscribe links are other slices' adapters.</summary>
public static class EmailChannelServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the email channel, its renderers and its own texts, and the startup check of every email kind. The host also registers an
        /// <see cref="IEmailSender"/> and, to send Marketing, an <see cref="IEmailUnsubscribeLinks"/>.
        /// </summary>
        public IServiceCollection AddEmailChannel()
        {
            services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(EmailChannel).Assembly));
            services.TryAddSingleton<IcuNotificationTexts>();
            services.TryAddSingleton<EmailComposer>();
            services.TryAddSingleton<Rendering.RazorEmailRenderer>();
            services.TryAddSingleton<IEmailUnsubscribeLinks, UnconfiguredUnsubscribeLinks>();
            services.AddSingleton<INotificationChannel, EmailChannel>();
            services.AddHostedService<EmailKindsCheck>();
            return services;
        }
    }
}
