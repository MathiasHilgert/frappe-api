using Frappe.Identity.Infrastructure;
using Frappe.Modularity;

namespace Frappe.Api;

/// <summary>Every business module the host runs. Adding a module is one line here.</summary>
internal static class Modules
{
    public static IReadOnlyList<IModule> All { get; } = [new IdentityModule()];
}
