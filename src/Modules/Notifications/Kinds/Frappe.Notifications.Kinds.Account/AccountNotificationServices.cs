using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Channels.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Kinds.Account;

/// <summary>Registers the account notification kinds and their texts in a host.</summary>
public static class AccountNotificationServices
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds <see cref="AccountActivationCode"/>, <see cref="PasswordResetCode"/> and <see cref="EmailChangeCode"/>.</summary>
        public IServiceCollection AddAccountNotifications()
        {
            services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(AccountActivationCode).Assembly));
            services.AddSingleton(NotificationKind.Of<AccountActivationCode, AccountActivationCodePayload>());
            services.AddSingleton(NotificationKind.Of<PasswordResetCode, PasswordResetCodePayload>());
            services.AddSingleton(NotificationKind.Of<EmailChangeCode, EmailChangeCodePayload>());
            return services;
        }
    }
}
