using Frappe.Identity.Contracts.Owners;
using Frappe.Modules;
using Frappe.Notifications.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Producers.Identity;

/// <summary>
/// The Worker's Identity producer (S2a, S18): it makes the facts Identity publishes into notifications, one durable consumer per event. The
/// consumer names are forever (<c>notifications-identity-&lt;purpose&gt;</c>). Identity knows nothing about Notifications; this plug-in is the
/// only place that knows both.
/// </summary>
public sealed class IdentityProducerModule : IModule
{
    /// <summary>The consumer of the code mails (activation, password reset, email change).</summary>
    public const string CodesConsumer = "notifications-identity-codes";

    /// <summary>The consumer of the notice to the real owner of a registered address.</summary>
    public const string RegistrationAttemptedConsumer = "notifications-identity-registration-attempted";

    /// <summary>The consumer of the notice to the old address of an email change.</summary>
    public const string EmailChangeRequestedConsumer = "notifications-identity-email-change-requested";

    /// <summary>The consumer of the undo notice to the old address of a confirmed email change.</summary>
    public const string EmailChangedConsumer = "notifications-identity-email-changed";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNotificationProducer<OwnerCodeIssued, OwnerCodeIssuedMapper>(CodesConsumer);
        services.AddNotificationProducer<OwnerRegistrationAttempted, OwnerRegistrationAttemptedMapper>(RegistrationAttemptedConsumer);
        services.AddNotificationProducer<OwnerEmailChangeRequested, OwnerEmailChangeRequestedMapper>(EmailChangeRequestedConsumer);
        services.AddNotificationProducer<OwnerEmailChanged, OwnerEmailChangedMapper>(EmailChangedConsumer);
        services.AddOptions<NotificationLinksOptions>().BindConfiguration(NotificationLinksOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NotificationLinksOptions>, NotificationLinksOptionsValidator>());
    }
}
