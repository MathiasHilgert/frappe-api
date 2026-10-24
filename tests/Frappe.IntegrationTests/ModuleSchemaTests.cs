using Frappe.Identity.Infrastructure;
using Frappe.Modularity;
using Frappe.Notification.Infrastructure;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>Each module owns one PostgreSQL schema, named like the module, and maps every table into it.</summary>
public sealed class ModuleSchemaTests(ApiFixture fixture)
{
    public static TheoryData<IModule> Modules => [new IdentityModule(), new NotificationModule()];

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task ModuleContext_MapsEveryTableIntoTheModulesOwnSchema(IModule module)
    {
        var contextType = module.GetType().Assembly.GetTypes()
            .SingleOrDefault(t => typeof(ModuleDbContext).IsAssignableFrom(t) && !t.IsAbstract);
        if (contextType is null)
            return; // A module without persistence owns no schema.

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var context = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);

        context.Schema.ShouldBe(module.Name);
        // Wolverine maps its envelope storage into every context, in its own schema; the module's own entities are the rule.
        context.Model.GetEntityTypes()
            .Where(e => e.ClrType.Namespace?.StartsWith("Frappe.", StringComparison.Ordinal) == true)
            .Select(e => e.GetSchema()).Distinct(StringComparer.Ordinal).ShouldBe([module.Name]);
        // No mapping reaches into another module: every Frappe entity (owned types included) is the module's own type.
        var ownNamespace = $"Frappe.{module.GetType().Assembly.GetName().Name!.Split('.')[1]}.";
        context.Model.GetEntityTypes()
            .Select(e => e.ClrType.Namespace)
            .Where(ns => ns?.StartsWith("Frappe.", StringComparison.Ordinal) == true)
            .ShouldAllBe(ns => ns!.StartsWith(ownNamespace, StringComparison.Ordinal));
    }
}
