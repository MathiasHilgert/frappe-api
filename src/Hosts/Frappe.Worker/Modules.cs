using Frappe.Access;
using Frappe.Identity;
using Frappe.Modules;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Kinds.Account;
using Frappe.Notifications.Kinds.Marketing;
using Frappe.Notifications.Producers.Identity;

namespace Frappe.Worker;

/// <summary>The modules the Worker runs, listed explicitly in one place: modules are never discovered by scanning.</summary>
public static class Modules
{
    /// <summary>Every module the Worker hosts, in registration order.</summary>
    public static IReadOnlyCollection<IModule> All { get; } =
    [
        AccessModule.Worker,
        IdentityModule.Worker,
        new NotificationsModule(),
        new NotificationsWorkerModule(),
        new AccountKindsModule(),
        new MarketingKindsModule(),
        new IdentityProducerModule(),
        new EmailChannelWorkerModule(),
    ];
}
