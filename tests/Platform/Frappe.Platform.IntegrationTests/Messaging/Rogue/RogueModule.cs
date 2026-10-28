using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Messaging.Rogue.Infrastructure;

// Breaks both handler boot checks; never registered in a running host.
public sealed class RogueModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new("rogue", "rogue", "rogue", new EventIdRange(6000, 6999), [], [], []);

    public void AddServices(IHostApplicationBuilder builder) { }

    public void MapEndpoints(RouteGroupBuilder group) { }
}
