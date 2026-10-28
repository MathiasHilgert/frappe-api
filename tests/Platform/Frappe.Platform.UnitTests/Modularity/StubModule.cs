using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.UnitTests.Modularity;

internal sealed class StubModule(ModuleMetadata metadata) : IModule
{
    public ModuleMetadata Metadata { get; } = metadata;

    public bool ServicesAdded { get; private set; }

    public static StubModule Named(
        string name,
        string? schema = null,
        string? routePrefix = null,
        EventIdRange? eventIds = null,
        Type[]? published = null,
        Type[]? subscribed = null,
        string[]? permissions = null
    ) =>
        new(
            new ModuleMetadata(
                name,
                schema ?? name,
                routePrefix ?? name,
                eventIds ?? new EventIdRange(1000, 1999),
                permissions ?? [],
                published ?? [],
                subscribed ?? []
            )
        );

    public void AddServices(IHostApplicationBuilder builder) => ServicesAdded = true;

    public void MapEndpoints(RouteGroupBuilder group) { }
}
