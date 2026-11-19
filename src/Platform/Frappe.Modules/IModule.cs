using Microsoft.Extensions.Hosting;

namespace Frappe.Modules;

/// <summary>
/// The services entry point of a module, implemented in its Infrastructure project. A host lists the modules it runs in
/// one explicit list and calls <see cref="AddServices"/> on each: modules are never discovered by scanning assemblies
/// (handlers still register through <c>AddHandlers(assembly)</c>). A host with an empty list builds and starts.
/// </summary>
public interface IModule
{
    /// <summary>The module's name, such as <c>Orders</c>: unique among the modules a host lists.</summary>
    string Name { get; }

    /// <summary>Registers everything the module needs in <paramref name="builder"/>: its persistence, handlers and background parts, and nothing of another module.</summary>
    void AddServices(IHostApplicationBuilder builder);
}
