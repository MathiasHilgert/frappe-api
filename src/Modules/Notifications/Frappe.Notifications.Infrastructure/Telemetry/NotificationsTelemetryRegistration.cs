using Frappe.Notifications.Application.Telemetry;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Domain.ProviderEvents;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Infrastructure.Telemetry;

/// <summary>Registers Notifications' metrics classes, one per feature, and what is counted once the unit that carried an event commits.</summary>
internal static class NotificationsTelemetryRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds one metrics class per feature, each created once over the host's meter factory.</summary>
        public IServiceCollection AddNotificationsMetrics()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddSingleton<NotificationsMetrics>();
            services.AddSingleton<ProviderFeedbackMetrics>();
            services.AddSingleton<PreferenceMetrics>();
            services.AddSingleton<RetentionMetrics>();
            return services;
        }

        /// <summary>
        /// Counts unsubscribes, provider events, suppressions and the time to delivered after the commit that carried them, never after a
        /// rollback (OBS-06).
        /// </summary>
        public IServiceCollection CountNotificationsEventsOnCommit()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.CountOnCommit<PreferenceOptedOut, PreferenceMetrics>((metrics, opted) => metrics.CountUnsubscribe(opted.Category));
            services.CountOnCommit<ProviderEventRecorded, ProviderFeedbackMetrics>((metrics, recorded) =>
                metrics.CountProviderEvent(recorded.Feedback, recorded.Matched));
            services.CountOnCommit<SuppressionAdded, ProviderFeedbackMetrics>((metrics, added) => metrics.CountSuppression(added.Reason));
            services.CountOnCommit<DeliveryDelivered, ProviderFeedbackMetrics>((metrics, delivered) =>
                metrics.RecordTimeToDelivered(delivered.Kind, delivered.Channel, delivered.TimeToDelivered));
            return services;
        }
    }
}
