using Frappe.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Http;

/// <summary>The HTTP facet of Identity. It maps no route yet: its endpoints arrive with the use cases they serve.</summary>
public sealed class IdentityHttp : IHttpModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services) => ArgumentNullException.ThrowIfNull(services);

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1) => ArgumentNullException.ThrowIfNull(v1);
}
