using Frappe.Notifications.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Channels.Email;

/// <summary>Registers what a host needs to receive Resend's webhooks.</summary>
public static class ResendWebhookServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the verifier and the parser of Resend's webhook (registered as the keyed <see cref="IProviderFeedbackParser"/>
        /// <see cref="ProviderFeedbackParsers.Resend"/>), and validates its signing secret when the host starts, naming the key (CFG-01).
        /// It needs no sender and no API key.
        /// </summary>
        public IServiceCollection AddResendWebhook()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<ResendWebhookOptions>().BindConfiguration(ResendWebhookOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ResendWebhookOptions>, ResendWebhookOptionsValidator>());
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<SvixSignatureVerifier>();
            services.AddKeyedSingleton<IProviderFeedbackParser, ResendFeedbackParser>(ProviderFeedbackParsers.Resend);
            return services;
        }
    }
}
