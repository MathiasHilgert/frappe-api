using Frappe.Identity.Infrastructure.Credentials;
using Frappe.Identity.Infrastructure.Messaging;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Modules;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Infrastructure;

/// <summary>
/// The services of the Identity module in every host: its database under the platform's limits, with the outbox that stages the module's
/// integration events in the same transaction as its data. What only the Worker runs is <see cref="IdentityWorkerModule"/>.
/// </summary>
public sealed class IdentityModule : IModule
{
    /// <summary>The lower-case module name of its namespaces (<c>Frappe.Identity.*</c>), also its connection string name.</summary>
    internal const string Name = "identity";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPasswords();
        services.AddKeyedSingleton<IIntegrationEventTranslator, IdentityIntegrationEvents>(Name);
        services.AddModuleDatabase<IdentityDbContext>(
            Name, IdentityDbContext.Schema, (provider, options) => options.UseOutbox(provider, Name));
    }
}
