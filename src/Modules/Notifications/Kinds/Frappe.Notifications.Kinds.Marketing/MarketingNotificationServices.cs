using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Channels.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Kinds.Marketing;

/// <summary>Registers the marketing notification kinds and their texts in a host.</summary>
public static class MarketingNotificationServices
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds <see cref="ProductAnnouncement"/>.</summary>
        public IServiceCollection AddMarketingNotifications()
        {
            services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(ProductAnnouncement).Assembly));
            services.AddSingleton(NotificationKind.Of<ProductAnnouncement, ProductAnnouncementPayload>());
            return services;
        }
    }
}
