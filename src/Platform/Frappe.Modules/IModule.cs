using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Modules;

/// <summary>
/// What a module's Infrastructure project contributes to a host: its services. A host lists its modules explicitly in
/// one place and calls <see cref="AddServices"/> on each; modules are never discovered by scanning.
/// </summary>
public interface IModule
{
    /// <summary>Registers the module's services in the host.</summary>
    void AddServices(IServiceCollection services);
}
