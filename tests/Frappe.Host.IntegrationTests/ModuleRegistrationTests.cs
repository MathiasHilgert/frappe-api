using System.Reflection;
using Alba;
using Frappe.Platform.Modularity;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Host.IntegrationTests;

public sealed class ModuleRegistrationTests(FrappeDatabase database)
{
    // Modules are listed by hand in Program, so a module whose class exists but is not listed would never run.
    [Fact]
    public async Task The_host_adds_every_module_it_ships()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder.UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
        );

        IEnumerable<Type> added = host
            .Services.GetServices<IModule>()
            .Select(static module => module.GetType());

        added.ShouldBe(ShippedModules(), ignoreOrder: true);
    }

    // Every Frappé assembly next to the Host, except the test projects; empty until the first module lands.
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
