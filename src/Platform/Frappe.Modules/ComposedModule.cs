using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Modules;

/// <summary>
/// One <see cref="IModule"/> made of several: a module's door hands a host one entry that registers each part's services, in order
/// (the services every host shares first, then the ones only that host runs).
/// </summary>
/// <param name="parts">The modules whose services this one registers, in registration order.</param>
public sealed class ComposedModule(params IReadOnlyList<IModule> parts) : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var part in parts)
        {
            part.AddServices(services);
        }
    }
}
