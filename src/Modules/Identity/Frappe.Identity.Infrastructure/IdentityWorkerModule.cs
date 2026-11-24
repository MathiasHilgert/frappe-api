using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Messaging.Abstractions;
using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Infrastructure;

/// <summary>
/// What only the Worker runs for Identity: the relay of its outbox and the purge of its messages. They are declared through the broker-free
/// messaging abstractions, so listing this module in a host without a transport records the declarations and runs nothing. The sweep of ended
/// sessions and the purge of stale data join it as their jobs arrive.
/// </summary>
public sealed class IdentityWorkerModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOutboxRelay<IdentityDbContext>(IdentityModule.Name);
        services.AddMessagePurge<IdentityDbContext>(IdentityModule.Name);
    }
}
