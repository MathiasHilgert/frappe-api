using Frappe.Access.Http;
using Frappe.Access.Infrastructure;
using Frappe.Http;
using Frappe.Identity.Http;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Limits.Valkey;
using Frappe.Modules;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Http;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Kinds.Account;
using Frappe.Notifications.Kinds.Marketing;

namespace Frappe.Api;

/// <summary>The modules the Api serves, listed explicitly in one place: modules are never discovered by scanning.</summary>
public static class Modules
{
    /// <summary>The services of every module the Api hosts, in registration order.</summary>
    public static IReadOnlyCollection<IModule> Services { get; } =
    [
        new AccessModule(),
        new IdentityModule(),
        new IdentityApiModule(),
        new IdentityLimitsValkeyModule(),
        new NotificationsModule(),
        new AccountKindsModule(),
        new MarketingKindsModule(),
        new EmailChannelApiModule(),
    ];

    /// <summary>The HTTP facet of every module the Api serves: its services and its endpoints under <c>/v1</c>.</summary>
    public static IReadOnlyCollection<IHttpModule> Endpoints { get; } = [new AccessHttp(), new IdentityHttp(), new NotificationsHttp()];
}
