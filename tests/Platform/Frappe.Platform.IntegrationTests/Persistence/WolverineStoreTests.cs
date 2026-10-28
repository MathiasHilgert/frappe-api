using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core.Migrations;
using Wolverine.Persistence.Durability;

namespace Frappe.Platform.IntegrationTests.Persistence;

// The host never creates the store (the app role runs no DDL), so the migration must create exactly what
// Wolverine expects. On a Wolverine upgrade this fails with the update DDL to put in a new migration.
public sealed class WolverineStoreTests(AppFixture app)
{
    [Fact]
    public async Task The_migrated_store_matches_what_the_deployed_configuration_expects()
    {
        // Production runs Balanced, whose store is the superset of Solo's; the host is built, never started.
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.Configuration["ConnectionStrings:Frappe"] = app.ConnectionString;
        builder.Configuration["Nats:Url"] = app.NatsUrl;
        builder.AddFrappePlatform();
        await using WebApplication host = builder.Build();
        var store = (IDatabase)host.Services.GetRequiredService<IMessageStore>();

        await Should.NotThrowAsync(() =>
            store.AssertDatabaseMatchesConfigurationAsync(TestContext.Current.CancellationToken)
        );
    }
}
