using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Infrastructure.Codes;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Modules;
using Frappe.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Infrastructure;

/// <summary>
/// What only the Api runs for Identity: issuing and redeeming one-time codes, which needs the code hash key and an attempt limiter, neither of
/// which the Worker has, and the use cases and services of sessions. Listed in the Api next to <see cref="IdentityModule"/> and the limiter plug-in.
/// </summary>
public sealed class IdentityApiModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCodes();
        services.AddSessions();
        services.AddHandlers(typeof(ISessions).Assembly);
    }
}
