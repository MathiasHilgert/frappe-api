using System.Data.Common;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.Modularity;

/// <summary>
/// A business module as the host composes it: what it declares, the services it registers and the endpoints it maps.
/// Each module has one implementation in its Infrastructure project, passed explicitly to <c>AddFrappeModules</c>.
/// </summary>
public interface IModule
{
    /// <summary>Gets what the module declares; checked at boot, before any service is registered.</summary>
    ModuleMetadata Metadata { get; }

    /// <summary>
    /// Registers the module's services, options and database context. The context owns the module's schema, so it is
    /// registered with <c>AddFrappeDbContext&lt;TContext&gt;(Metadata.Schema)</c> and passes the same schema to its
    /// base constructor.
    /// </summary>
    /// <param name="builder">The application builder, for its services and configuration.</param>
    void AddServices(IHostApplicationBuilder builder);

    /// <summary>
    /// Maps the module's endpoints inside its group, <c>/v1/{RoutePrefix}</c> tagged with its name; called only in
    /// the roles that serve business endpoints.
    /// </summary>
    /// <param name="group">The module's route group.</param>
    void MapEndpoints(RouteGroupBuilder group);

    // TODO: the jobs PR adds the module's recurring schedules here as a default interface method, so a module
    // without schedules implements nothing; registered only in the roles that run schedules.

    /// <summary>
    /// Loads the module's reference data and returns the rows it changed. The migrator runs it as the owner role
    /// after every deployment's migrations, inside a transaction of its own, so it must be idempotent: upsert by
    /// natural key (<c>INSERT ... ON CONFLICT ... DO UPDATE ... WHERE ... IS DISTINCT FROM excluded</c>, so an
    /// unchanged row counts as nothing) and never delete. A module without reference data implements nothing.
    /// </summary>
    /// <param name="connection">The open owner connection; its commands run in the module's transaction.</param>
    /// <param name="cancellationToken">Cancels the seed and rolls back the module's transaction.</param>
    Task<int> SeedAsync(DbConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
