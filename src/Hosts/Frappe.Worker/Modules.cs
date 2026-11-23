using Frappe.Modules;

namespace Frappe.Worker;

/// <summary>The modules the Worker runs, listed explicitly in one place: modules are never discovered by scanning.</summary>
public static class Modules
{
    /// <summary>Every module the Worker hosts, in registration order.</summary>
    public static IReadOnlyCollection<IModule> All { get; } = [];
}
