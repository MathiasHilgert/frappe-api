using Frappe.Http;
using Frappe.Notifications.Http.Sends;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Notifications.Http;

/// <summary>
/// The HTTP facet of Notifications. Its explicit send route is mapped only when the host environment is Development: it is a composition
/// decision made here, not a use-case rule, and the command behind it stays the same when the campaigns API arrives (S1, S2, S10).
/// </summary>
public sealed class NotificationsHttp : IHttpModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddErrorMessages(typeof(NotificationsHttp).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);
        if (v1.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            v1.MapSendNotificationEndpoints();
        }
    }
}
