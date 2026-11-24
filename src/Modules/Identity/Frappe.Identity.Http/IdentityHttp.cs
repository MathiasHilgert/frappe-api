using Frappe.Http;
using Frappe.Identity.Http.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Identity.Http;

/// <summary>
/// The HTTP facet of Identity: the session scheme and its policy, which only routes that ask for a session use (SEC-01), the error texts, and
/// the routes under <c>/v1/identity</c>.
/// </summary>
public sealed class IdentityHttp : IHttpModule
{
    /// <summary>The OpenAPI tag of the module's operations.</summary>
    internal const string Tag = "Identity";

    private const string Prefix = "/identity";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddErrorMessages(typeof(IdentityHttp).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSessionAuthentication();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);
        var identity = v1.MapGroup(Prefix);
        identity.MapSessionEndpoints();
    }
}
