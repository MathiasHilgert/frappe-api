using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;

// A fixture module that consumes TicketOpened.
public sealed class AuditModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new(
            "audit",
            "audit",
            "audit",
            new EventIdRange(5000, 5999),
            [],
            [],
            [typeof(TicketOpened)]
        );

    public void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddFrappeDbContext<AuditDbContext>(Metadata.Schema);

    public void MapEndpoints(RouteGroupBuilder group) { }
}
