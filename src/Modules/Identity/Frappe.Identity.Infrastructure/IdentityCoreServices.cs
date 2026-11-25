using Frappe.Identity.Infrastructure.Credentials;
using Frappe.Identity.Infrastructure.Messaging;
using Frappe.Identity.Infrastructure.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Identity.Infrastructure.Telemetry;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Modules;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Infrastructure;

/// <summary>
/// The services of the Identity module in every host: its database under the platform's limits, with the outbox that stages the module's
/// integration events in the same transaction as its data. Composed by the door (<c>IdentityModule</c>);
/// what only the Api runs is <see cref="IdentityApiServices"/>, what only the Worker runs is <see cref="IdentityWorkerServices"/>.
/// </summary>
public sealed class IdentityCoreServices : IModule
{
    /// <summary>The lower-case module name of its namespaces (<c>Frappe.Identity.*</c>), also its connection string name.</summary>
    internal const string Name = "identity";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddIdentityMetrics();
        services.AddPasswords();
        services.AddKeyedSingleton<IIntegrationEventTranslator, IdentityIntegrationEvents>(Name);
        services.AddCommitRejections<IdentityDbContext>(rejections =>
        {
            // Owners' rules go first: the session rule on a concurrency failure rejects whatever it sees.
            OwnerRejections.Register(rejections);
            SessionRejections.Register(rejections);
        });
        services.AddModuleDatabase<IdentityDbContext>(
            Name, IdentityDbContext.Schema, (provider, options) => options.UseOutbox(provider, Name));
    }
}
