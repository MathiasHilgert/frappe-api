using Frappe.Access;
using Frappe.Http;
using Frappe.Identity;
using Frappe.Notifications;

namespace Frappe.Api;

/// <summary>The modules the Api serves, listed explicitly in one place: modules are never discovered by scanning.</summary>
public static class Modules
{
    /// <summary>
    /// The door of every module the Api serves (<c>&lt;M&gt;Module.Api</c>): its services and its endpoints under <c>/v1</c>, in registration order.
    /// </summary>
    public static IReadOnlyCollection<IHttpModule> Endpoints { get; } = [AccessModule.Api, IdentityModule.Api, NotificationsModule.Api];
}
