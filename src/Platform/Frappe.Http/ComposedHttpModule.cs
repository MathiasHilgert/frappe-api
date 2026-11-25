using Frappe.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Http;

/// <summary>
/// One <see cref="IHttpModule"/> made of a module's services and its HTTP facet: a module's door hands the Api one entry that registers
/// <paramref name="modules"/> in order, then <paramref name="http"/>'s services, and maps <paramref name="http"/>'s endpoints.
/// </summary>
/// <param name="modules">The module's services the Api runs (the ones every host shares first), in registration order.</param>
/// <param name="http">The module's HTTP facet: its HTTP services and its endpoints.</param>
public sealed class ComposedHttpModule(IReadOnlyList<IModule> modules, IHttpModule http) : IHttpModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var module in modules)
        {
            module.AddServices(services);
        }

        http.AddServices(services);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1) => http.MapEndpoints(v1);
}
