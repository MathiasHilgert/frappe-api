using Frappe.Http;
using Frappe.Identity.Http;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Limits.Valkey;
using Frappe.Modules;

namespace Frappe.Identity;

/// <summary>
/// The door of Identity: the only thing a host names from the module. <see cref="Api"/> serves it in the Api and <see cref="Worker"/> runs it
/// in the Worker; both start from the services every host shares (<see cref="IdentityCoreServices"/>) and add only what their host runs.
/// </summary>
public static class IdentityModule
{
    /// <summary>
    /// Identity in the Api: the shared services, the one-time codes, sessions and owners (<see cref="IdentityApiServices"/>), the attempt
    /// limits counted in Valkey (<see cref="IdentityLimitsValkeyModule"/>), then its HTTP facet (<see cref="IdentityHttp"/>): the session
    /// scheme, the session cookie as the browser credential the console origin check asks about, and the routes under <c>/v1/identity</c>.
    /// </summary>
    public static IHttpModule Api { get; } =
        new ComposedHttpModule([new IdentityCoreServices(), new IdentityApiServices(), new IdentityLimitsValkeyModule()], new IdentityHttp());

    /// <summary>
    /// Identity in the Worker: the shared services, the outbox relay and the daily retention (<see cref="IdentityWorkerServices"/>), and the
    /// purge of its outbox and inbox messages the retention runs (<see cref="IdentityMessagePurgeServices"/>).
    /// </summary>
    public static IModule Worker { get; } =
        new ComposedModule(new IdentityCoreServices(), new IdentityWorkerServices(), new IdentityMessagePurgeServices());
}
