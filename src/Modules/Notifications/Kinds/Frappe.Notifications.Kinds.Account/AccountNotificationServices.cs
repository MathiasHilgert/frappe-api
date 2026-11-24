using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Channels.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Kinds.Account;

/// <summary>Registers the account notification kinds and their texts in a host.</summary>
public static class AccountNotificationServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the three code kinds, <see cref="RegistrationAttempted"/>, <see cref="EmailChangeRequested"/> and <see cref="EmailChanged"/>.
        /// </summary>
        public IServiceCollection AddAccountNotifications()
        {
            services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(AccountActivationCode).Assembly));
            services.AddSingleton(NotificationKind.Of<AccountActivationCode, AccountActivationCodePayload>());
            services.AddSingleton(NotificationKind.Of<PasswordResetCode, PasswordResetCodePayload>());
            services.AddSingleton(NotificationKind.Of<EmailChangeCode, EmailChangeCodePayload>());
            services.AddSingleton(NotificationKind.Of<RegistrationAttempted, RegistrationAttemptedPayload>());
            services.AddSingleton(NotificationKind.Of<EmailChangeRequested, EmailChangeRequestedPayload>());
            services.AddSingleton(NotificationKind.Of<EmailChanged, EmailChangedPayload>());
            return services;
        }
    }
}
