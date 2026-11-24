using Frappe.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Http;

/// <summary>The HTTP facet of Notifications. It maps no route yet: the routes arrive with the commands they serve.</summary>
public sealed class NotificationsHttp : IHttpModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services) => ArgumentNullException.ThrowIfNull(services);

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1) => ArgumentNullException.ThrowIfNull(v1);
}
