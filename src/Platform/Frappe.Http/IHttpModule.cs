using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Http;

/// <summary>
/// What a module's Http project contributes to the API host: its services and its endpoints under <c>/v1</c>. A host lists
/// its HTTP modules explicitly in one place; modules are never discovered by scanning.
/// </summary>
public interface IHttpModule
{
    /// <summary>Registers the module's HTTP services in the host.</summary>
    void AddServices(IServiceCollection services);

    /// <summary>Maps the module's endpoints in <paramref name="v1"/>, the group that carries the API's shared conventions.</summary>
    void MapEndpoints(IEndpointRouteBuilder v1);
}
