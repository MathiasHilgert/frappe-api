using Frappe.Http;
using Frappe.Modules;
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
        [new NotificationsModule(), new AccountKindsModule(), new MarketingKindsModule()];

    /// <summary>The HTTP facet of every module the Api serves: its services and its endpoints under <c>/v1</c>.</summary>
    public static IReadOnlyCollection<IHttpModule> Endpoints { get; } = [new NotificationsHttp()];
}
