using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Frappe.Modularity;

/// <summary>Composition entry point of a business module. The host knows modules only through this.</summary>
public interface IModule
{
    /// <summary>Lowercase name; also the route segment under <c>/v1</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Assemblies Wolverine scans for handlers and message types: the module's Application and Contracts.
    /// A message type outside them has no route and is silently dropped.
    /// </summary>
    IReadOnlyList<Assembly> MessagingAssemblies { get; }

    /// <summary>
    /// Names of the meters the module publishes. The host exports them, so it never references a module's internals
    /// to wire observability.
    /// </summary>
    IReadOnlyList<string> Meters => [];

    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Module-specific routing, queues and retry policies.</summary>
    void ConfigureMessaging(WolverineOptions options)
    {
    }

    /// <summary>Receives the module's own <c>/v1/{Name}</c> group, so prefixes and conventions stay uniform.</summary>
    void MapEndpoints(RouteGroupBuilder group)
    {
    }
}
