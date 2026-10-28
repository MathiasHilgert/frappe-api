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
    // TODO: the seed command PR adds the module's idempotent seed step here as a default interface method, so a
    // module without seed data implements nothing; run by the seed subcommand.
}
