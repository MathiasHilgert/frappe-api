using Frappe.Platform.Hosting;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.IntegrationTests.Persistence;

// A host that registers persistence on its own, without the platform composition.
public sealed class PersistenceRegistrationTests(AppFixture app)
{
    [Fact]
    public async Task Persistence_alone_names_connections_after_the_configured_role()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration["Platform:Role"] = "worker";
        builder.Configuration["ConnectionStrings:Frappe"] = app.ConnectionString;
        builder.AddPersistence();
        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        string name = await db
            .Database.SqlQuery<string>($"SELECT current_setting('application_name') AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        name.ShouldBe("frappe-worker");
    }

    [Fact]
    public async Task Persistence_alone_still_rejects_an_invalid_role()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration["Platform:Role"] = "7";
        builder.Configuration["ConnectionStrings:Frappe"] = app.ConnectionString;
        builder.AddPersistence();
        using IHost host = builder.Build();

        OptionsValidationException error = await Should.ThrowAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        error.Message.ShouldContain("Role");
    }

    [Fact]
    public void Persistence_and_the_platform_together_bind_the_role_once()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );

        // The platform first: the bus must be registered before any context.
        builder.AddFrappePlatform();
        builder.AddPersistence();

        builder
            .Services.Count(static service =>
                service.ServiceType == typeof(IConfigureOptions<PlatformOptions>)
            )
            .ShouldBe(1);
        builder
            .Services.Count(static service =>
                service.ServiceType == typeof(IOptionsChangeTokenSource<PlatformOptions>)
            )
            .ShouldBe(1);
    }
}
