using Frappe.Notifications.Application.Messages;
using Frappe.Notifications.Application.Senders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Senders.Email;

/// <summary>Registers the email channel in a host. The sender is another slice's adapter; the unsubscribe links use the module's tokens.</summary>
public static class EmailChannelServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds what trying a payload as an email needs (the texts, the composer and <see cref="EmailPayloadCheck"/>), for a host that accepts
        /// requests and never sends them, so a url that is not https is refused at the route.
        /// </summary>
        public IServiceCollection AddEmailPayloadCheck()
        {
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(EmailComposer)))
            {
                services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(EmailChannel).Assembly));
            }

            services.TryAddSingleton<IcuNotificationTexts>();
            services.TryAddSingleton<EmailComposer>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationPayloadCheck, EmailPayloadCheck>());
            return services;
        }

        /// <summary>
        /// Adds the email channel, its renderers and its own texts, and the startup check of every email kind. The host also registers an
        /// <see cref="IEmailSender"/>, and needs the module's <c>IUnsubscribeTokens</c> and <c>Notifications:Unsubscribe</c> (the page and the API's
        /// address) to make a Marketing mail's links, which it validates when the host starts.
        /// </summary>
        public IServiceCollection AddEmailChannel()
        {
            services.AddEmailPayloadCheck();
            services.TryAddSingleton<Rendering.RazorEmailRenderer>();
            services.AddOptions<UnsubscribeLinkOptions>().BindConfiguration(UnsubscribeLinkOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UnsubscribeLinkOptions>, UnsubscribeLinkOptionsValidator>());
            services.TryAddSingleton<IEmailUnsubscribeLinks, SignedUnsubscribeLinks>();
            services.AddSingleton<INotificationChannel, EmailChannel>();
            services.AddHostedService<EmailKindsCheck>();
            return services;
        }
    }
}
