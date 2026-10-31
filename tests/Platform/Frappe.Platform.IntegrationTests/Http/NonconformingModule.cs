using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Http;

// A fake module that breaks every route rule once, so the tests see the standard catch each one.
internal sealed class NonconformingModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new("rogue", "rogue", "rogues", new EventIdRange(1000, 1999), [], [], []);

    public void AddServices(IHostApplicationBuilder builder) { }

    public void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/create-rogue", static () => TypedResults.NoContent()).AllowAnonymous();
        group
            .MapGet("/Hidden_Things", static () => TypedResults.NoContent())
            .WithName("GetHiddenThings")
            .RequireRateLimiting("public")
            .ExcludeFromDescription();
    }
}
