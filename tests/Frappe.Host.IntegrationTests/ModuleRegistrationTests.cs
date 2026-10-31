using System.Reflection;
using Alba;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Frappe.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Host.IntegrationTests;

public sealed class ModuleRegistrationTests(FrappeDatabase database, FrappeNats nats)
{
    // Modules are listed by hand in Program, so a module whose class exists but is not listed would never run.
    [Fact]
    public async Task The_host_adds_every_module_it_ships()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
                .UseSetting("Nats:Url", nats.Url)
        );

        IEnumerable<Type> added = host
            .Services.GetServices<IModule>()
            .Select(static module => module.GetType());

        added.ShouldBe(ShippedModules(), ignoreOrder: true);
    }

    // A module's context must own its module's schema, or its migrations would land in another one.
    [Fact]
    public async Task Each_module_context_owns_its_module_schema()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
                .UseSetting("Nats:Url", nats.Url)
        );
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

        foreach (IModule module in host.Services.GetServices<IModule>())
        {
            IEnumerable<Type> contexts = module
                .GetType()
                .Assembly.GetTypes()
                .Where(static type =>
                    type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(FrappeDbContext))
                );
            foreach (Type context in contexts)
            {
                var resolved = (DbContext)scope.ServiceProvider.GetRequiredService(context);
                resolved.Model.GetDefaultSchema().ShouldBe(module.Metadata.Schema, context.Name);
            }
        }
    }

    // Every Frappé assembly next to the Host, except the test projects.
    private static IEnumerable<Type> ShippedModules() =>
        Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .Where(static name =>
                !name.EndsWith("Tests", StringComparison.Ordinal)
                && !string.Equals(name, "Frappe.TestUtilities", StringComparison.Ordinal)
            )
            .Select(static name => Assembly.Load(name))
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type =>
                type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(IModule))
            );
}
