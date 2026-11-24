using Frappe.Messaging.Abstractions;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Modules;
using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Domain.ProviderEvents;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Notifications.Infrastructure.Addresses;
using Frappe.Notifications.Infrastructure.Dispatching;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Infrastructure.Requests;
using Frappe.Notifications.Infrastructure.Unsubscribe;
using Frappe.Observability;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Infrastructure;

/// <summary>
/// The services of the Notifications module in every host: its database under the platform's limits, with the outbox that stages
/// the module's integration events in the same transaction as its data, and the declarations of its relay and purge, which only
/// the Worker's transport materializes.
/// </summary>
public sealed class NotificationsModule : IModule
{
    /// <summary>The lower-case module name of its namespaces (<c>Frappe.Notifications.*</c>), also its connection string name.</summary>
    internal const string Name = "notifications";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddKeyedSingleton<IIntegrationEventTranslator, NotificationsEventTranslator>(Name);
        services.AddCommitRejections<NotificationsDbContext>(rejections =>
        {
            FeedbackRejections.Register(rejections);
            PreferenceRejections.Register(rejections);
        });
        services.AddModuleDatabase<NotificationsDbContext>(
            Name, NotificationsDbContext.Schema, (provider, options) => options.UseOutbox(provider, Name));
        services.AddOutboxRelay<NotificationsDbContext>(Name);
        services.AddMessagePurge<NotificationsDbContext>(Name);
        services.AddScoped<ISendRequests, EfSendRequests>();
        services.AddHandlers(typeof(ISendRequests).Assembly);
        AddDispatch(services);
    }

    /// <summary>
    /// The dispatch pipeline: its steps, the stores they read and the staging they write through. Kinds and channels are contributed by
    /// plug-ins (<c>NotificationKind</c> and <c>INotificationChannel</c> registrations); the contact resolvers by whoever owns the addresses.
    /// </summary>
    private static void AddDispatch(IServiceCollection services)
    {
        services.AddOptions<AddressHashOptions>().BindConfiguration(AddressHashOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AddressHashOptions>, AddressHashOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAddressHasher, HmacAddressHasher>();
        services.AddOptions<UnsubscribeOptions>().BindConfiguration(UnsubscribeOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UnsubscribeOptions>, UnsubscribeOptionsValidator>());
        services.AddSingleton<IUnsubscribeTokens, HmacUnsubscribeTokens>();
        services.AddSingleton<IContactDirectory, ContactDirectory>();
        services.AddSingleton<NotificationCatalog>();
        services.AddHostedService<NotificationCatalogCheck>();
        services.AddSingleton<NotificationsMetrics>();
        services.AddSingleton<ProviderFeedbackMetrics>();
        services.AddSingleton<PreferenceMetrics>();
        services.CountOnCommit<PreferenceOptedOut, PreferenceMetrics>((metrics, opted) => metrics.CountUnsubscribe(opted.Category));
        services.CountOnCommit<ProviderEventRecorded, ProviderFeedbackMetrics>((metrics, recorded) =>
            metrics.CountProviderEvent(recorded.Feedback, recorded.Matched));
        services.CountOnCommit<SuppressionAdded, ProviderFeedbackMetrics>((metrics, added) => metrics.CountSuppression(added.Reason));
        services.CountOnCommit<DeliveryDelivered, ProviderFeedbackMetrics>((metrics, delivered) =>
            metrics.RecordTimeToDelivered(delivered.Kind, delivered.Channel, delivered.TimeToDelivered));
        services.AddScoped<IDeliveryStaging, EfDeliveryStaging>();
        services.AddScoped<IDeliveries, EfDeliveries>();
        services.AddScoped<IProviderMessageLock, EfProviderMessageLock>();
        services.AddScoped<ProviderFeedbackIntake>();
        services.AddScoped<IProviderEvents, EfProviderEvents>();
        services.AddScoped<ISuppressionStore, EfSuppressionStore>();
        services.AddScoped<IPreferenceStore, EfPreferenceStore>();
        services.AddScoped<CatalogDispatch>();
        services.AddScoped<ParkedFeedbackApplier>();
        services.AddScoped<RecipientResolution>();
        services.AddScoped<PreferenceGate>();
        services.AddScoped<ChannelSelection>();
        services.AddScoped<SuppressionGate>();
        services.AddScoped<DeliveryRecorder>();
        services.AddScoped<NotificationDispatcher>();
    }
}
