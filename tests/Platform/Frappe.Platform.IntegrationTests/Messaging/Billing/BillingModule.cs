using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;

// A fixture module that consumes TicketOpened.
public sealed class BillingModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new(
            "billing",
            "billing",
            "billing",
            new EventIdRange(4000, 4999),
            [],
            [],
            [typeof(TicketOpened)]
        );

    public void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddFrappeDbContext<BillingDbContext>(Metadata.Schema);

    public void MapEndpoints(RouteGroupBuilder group) { }
}
