using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;

// A fixture module that opens tickets and publishes TicketOpened; its namespace root, Kitchen, holds its code.
public sealed class KitchenModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new(
            "kitchen",
            "kitchen",
            "kitchen",
            new EventIdRange(3000, 3999),
            [],
            [typeof(TicketOpened)],
            []
        );

    public void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddFrappeDbContext<KitchenDbContext>(Metadata.Schema);

    public void MapEndpoints(RouteGroupBuilder group) { }
}
