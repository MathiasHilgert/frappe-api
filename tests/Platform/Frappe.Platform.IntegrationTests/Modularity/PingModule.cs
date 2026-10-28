using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Modularity;

// A fake module with one endpoint, so the tests see where the platform puts module routes.
internal sealed class PingModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new("ping", "ping", "pings", new EventIdRange(1000, 1999), [], [], []);

    public void AddServices(IHostApplicationBuilder builder) { }

    public void MapEndpoints(RouteGroupBuilder group) =>
        group.MapGet("/latest", static () => TypedResults.Text("pong"));
}
