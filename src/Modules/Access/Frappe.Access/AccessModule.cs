using Frappe.Access.Http;
using Frappe.Access.Infrastructure;
using Frappe.Http;
using Frappe.Modules;

namespace Frappe.Access;

/// <summary>
/// The door of Access: the only thing a host names from the module. <see cref="Api"/> serves it in the Api and <see cref="Worker"/> runs it
/// in the Worker; both start from the services every host shares (<see cref="AccessCoreServices"/>) and add only what their host runs.
/// </summary>
public static class AccessModule
{
    /// <summary>Access in the Api: the shared services, then its HTTP facet (<see cref="AccessHttp"/>), the texts of its errors.</summary>
    public static IHttpModule Api { get; } = new ComposedHttpModule([new AccessCoreServices()], new AccessHttp());

    /// <summary>Access in the Worker: the shared services, then the system caller (<see cref="AccessWorkerServices"/>).</summary>
    public static IModule Worker { get; } = new ComposedModule(new AccessCoreServices(), new AccessWorkerServices());
}
