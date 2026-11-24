using Frappe.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Access.Http;

/// <summary>
/// The HTTP facet of Access: it owns the texts of the <c>access.*</c> errors and maps no routes, because authorization is a decision about
/// other modules' routes, never a route of its own.
/// </summary>
public sealed class AccessHttp : IHttpModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddErrorMessages(typeof(AccessHttp).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
        // No routes (S4).
    }
}
