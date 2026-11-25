using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity;

/// <summary>
/// Registers <see cref="IdentityMessagePurge"/> as the message purge the Identity retention runs. Only the Worker composes it, after
/// <c>IdentityWorkerServices</c>, which declares the purger it resolves.
/// </summary>
internal sealed class IdentityMessagePurgeServices : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Infrastructure.Retention.IMessagePurge, IdentityMessagePurge>();
    }
}
