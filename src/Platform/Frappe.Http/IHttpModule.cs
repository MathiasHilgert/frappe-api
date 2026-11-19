using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Http;

/// <summary>
/// The Api facet of a module. A module implements it in its own Http project, and the Api host lists the module in
/// one place: the host calls <see cref="AddServices"/> while it composes the services and <see cref="MapEndpoints"/>
/// while it maps the routes, so a module is exposed through a single door per host.
/// </summary>
public interface IHttpModule
{
    /// <summary>Registers what the module's endpoints need (message resources, validators, option bindings).</summary>
    /// <param name="services">The service collection of the host.</param>
    void AddServices(IServiceCollection services);

    /// <summary>
    /// Maps the module's endpoints under the version group of the host. The module maps routes only under its own
    /// first segment (for example <c>/orders</c>) and sets one OpenAPI tag (HTTP-02).
    /// </summary>
    /// <param name="v1">The <c>/v1</c> route group; it already negotiates the request language.</param>
    void MapEndpoints(IEndpointRouteBuilder v1);
}
