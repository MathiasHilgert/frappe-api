using Frappe.Access.Contracts;
using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Access.Infrastructure;

/// <summary>
/// What only the Worker adds for Access: its callers are the platform itself (jobs and consumers), registered as <see cref="Caller.System"/>.
/// A system caller is no principal, so it never passes a guarded rule implicitly.
/// </summary>
public sealed class AccessWorkerModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICaller>(Caller.System);
    }
}
