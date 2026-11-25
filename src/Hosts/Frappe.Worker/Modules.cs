using Frappe.Access;
using Frappe.Identity;
using Frappe.Modules;
using Frappe.Notifications;

namespace Frappe.Worker;

/// <summary>The modules the Worker runs, listed explicitly in one place: modules are never discovered by scanning.</summary>
public static class Modules
{
    /// <summary>The door of every module the Worker hosts (<c>&lt;M&gt;Module.Worker</c>), in registration order.</summary>
    public static IReadOnlyCollection<IModule> All { get; } = [AccessModule.Worker, IdentityModule.Worker, NotificationsModule.Worker];
}
